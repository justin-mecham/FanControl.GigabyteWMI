using System;
using System.IO;
using System.Management;

namespace FanControl.GigabyteWMI.Hardware
{
    /// <summary>
    /// Low-level concrete WMI ACPI driver for Gigabyte motherboard firmware.
    /// Manages elevated ManagementScope, method parameter marshaling, and diagnostic logging.
    /// </summary>
    public class GigabyteWmiDriver : IGigabyteWmiDriver
    {
        private ManagementClass? _mcSet;
        private ManagementClass? _mcGet;
        private ManagementObject? _setInstance;
        private ManagementObject? _getInstance;
        private ManagementScope? _scope;
        private static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "fancontrol_gigabyte_debug.log");

        public bool EnsureConnected()
        {
            try
            {
                if (_scope == null || !_scope.IsConnected || _setInstance == null || _getInstance == null)
                {
                    var co = new ConnectionOptions
                    {
                        EnablePrivileges = true,
                        Impersonation = ImpersonationLevel.Impersonate
                    };
                    _scope = new ManagementScope(@"root\WMI", co);
                    _scope.Connect();

                    _mcSet = new ManagementClass(_scope, new ManagementPath("GB_WMIACPI_Set"), null);
                    _mcGet = new ManagementClass(_scope, new ManagementPath("GB_WMIACPI_Get"), null);

                    _setInstance = null;
                    foreach (ManagementObject inst in _mcSet.GetInstances())
                    {
                        _setInstance = inst;
                        break;
                    }

                    _getInstance = null;
                    foreach (ManagementObject inst in _mcGet.GetInstances())
                    {
                        _getInstance = inst;
                        break;
                    }

                    Log("Connected to root\\WMI scope with EnablePrivileges (cached instances).");
                }
                return _setInstance != null && _getInstance != null;
            }
            catch (Exception ex)
            {
                Log("EnsureConnected error: " + ex.Message);
                return false;
            }
        }

        public void SetMethod(string methodName, byte value)
        {
            try
            {
                if (_setInstance == null)
                {
                    if (!EnsureConnected() || _setInstance == null) return;
                }

                ManagementBaseObject inParams = _setInstance.GetMethodParameters(methodName);
                inParams["Data"] = value;
                _setInstance.InvokeMethod(methodName, inParams, null);
            }
            catch (Exception ex)
            {
                Log($"SetMethod {methodName}({value}) failed: {ex.Message}");
                InvalidateOnFailure(ex);
            }
        }

        public uint GetMethod(string methodName)
        {
            try
            {
                if (_getInstance == null)
                {
                    if (!EnsureConnected() || _getInstance == null) return 0;
                }

                ManagementBaseObject outParams = _getInstance.InvokeMethod(methodName, null, null);
                if (outParams != null && outParams["Data"] != null)
                {
                    return Convert.ToUInt32(outParams["Data"]);
                }
            }
            catch (Exception ex)
            {
                Log($"GetMethod {methodName} failed: {ex.Message}");
                InvalidateOnFailure(ex);
            }
            return 0;
        }

        /// <summary>
        /// Drops cached WMI objects after a connection-shaped failure so the next call
        /// reconnects (stale handles after sleep/resume otherwise fail silently forever).
        /// A method that simply doesn't exist keeps the connection - reconnecting won't fix it.
        /// </summary>
        private void InvalidateOnFailure(Exception ex)
        {
            if (ex is ManagementException mex &&
                (mex.ErrorCode == ManagementStatus.NotFound ||
                 mex.ErrorCode == ManagementStatus.MethodNotImplemented ||
                 mex.ErrorCode == ManagementStatus.InvalidMethod ||
                 mex.ErrorCode == ManagementStatus.InvalidMethodParameters))
            {
                return;
            }

            _setInstance = null;
            _getInstance = null;
            _mcSet = null;
            _mcGet = null;
            _scope = null;
        }

        public void Log(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message;
            try
            {
                File.AppendAllText(DebugLogPath, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
