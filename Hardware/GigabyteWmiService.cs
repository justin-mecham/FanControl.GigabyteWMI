using System;
using System.IO;
using System.Management;

namespace FanControl.GigabyteWMI.Hardware
{
    /// <summary>
    /// Exact proven WMI ACPI communication service.
    /// Uses elevated ManagementScope with EnablePrivileges for guaranteed ACPI method execution.
    /// </summary>
    public class GigabyteWmiService : IGigabyteWmiService
    {
        private ManagementClass? _mcSet;
        private ManagementClass? _mcGet;
        private ManagementScope? _scope;
        private static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "fancontrol_gigabyte_debug.log");

        public bool EnsureConnected()
        {
            try
            {
                if (_scope == null || !_scope.IsConnected)
                {
                    ConnectionOptions co = new ConnectionOptions
                    {
                        EnablePrivileges = true,
                        Impersonation = ImpersonationLevel.Impersonate
                    };
                    _scope = new ManagementScope(@"root\WMI", co);
                    _scope.Connect();
                    _mcSet = new ManagementClass(_scope, new ManagementPath("GB_WMIACPI_Set"), null);
                    _mcGet = new ManagementClass(_scope, new ManagementPath("GB_WMIACPI_Get"), null);
                    Log("Connected to root\\WMI scope with EnablePrivileges.");
                }
                return _mcSet != null && _mcGet != null;
            }
            catch (Exception ex)
            {
                Log("EnsureConnected error: " + ex.Message);
                return false;
            }
        }

        public void EnterManualMode()
        {
            InvokeSet("SetTppStatus", 0);
            InvokeSet("SetTurboMode", 0);
            InvokeSet("SetWhisperMode", 0);
            InvokeSet("SetAutoFanStatus", 0);
            InvokeSet("SetStepFanStatus", 1);
            InvokeSet("SetFixedFanStatus", 1);
            Log("Entered Manual Hardware Mode (StepFanStatus=1, FixedFanStatus=1).");
        }

        public void SetFanSpeeds(byte cpuRegSpeed, byte gpuDuty)
        {
            InvokeSet("SetFixedFanSpeed", cpuRegSpeed);
            InvokeSet("SetGPUFanDuty", gpuDuty);
            Log($"Updated Fan Speeds -> CPU RegSpeed: {cpuRegSpeed}, GPU Duty: {gpuDuty}%");
        }

        public void ApplyTurboMode()
        {
            InvokeSet("SetAutoFanStatus", 0);
            InvokeSet("SetWhisperMode", 0);
            InvokeSet("SetStepFanStatus", 1);
            InvokeSet("SetFixedFanStatus", 1);
            InvokeSet("SetFixedFanSpeed", 229);
            InvokeSet("SetGPUFanDuty", 100);
            InvokeSet("SetTurboMode", 1);
            InvokeSet("SetTppStatus", 1);
            Log("Applied 100% MAX Turbo Mode (7500 RPM TPP Active).");
        }

        public void ExitTurboMode()
        {
            InvokeSet("SetTurboMode", 0);
            InvokeSet("SetTppStatus", 0);
            InvokeSet("SetWhisperMode", 0);
            InvokeSet("SetAutoFanStatus", 0);
            InvokeSet("SetStepFanStatus", 1);
            InvokeSet("SetFixedFanStatus", 1);
            Log("Exited Turbo Mode (Released TPP hardware latch).");
        }

        public void ApplySilentEcoMode()
        {
            InvokeSet("SetTurboMode", 0);
            InvokeSet("SetTppStatus", 0);
            InvokeSet("SetFixedFanStatus", 0);
            InvokeSet("SetStepFanStatus", 0);
            InvokeSet("SetAutoFanStatus", 1);
            InvokeSet("SetFixedFanSpeed", 0);
            InvokeSet("SetGPUFanDuty", 0);
            InvokeSet("SetWhisperMode", 1);
            Log("Applied Silent / Whisper Eco Mode (~1600 RPM).");
        }

        public void RestoreAutoMode()
        {
            InvokeSet("SetTurboMode", 0);
            InvokeSet("SetTppStatus", 0);
            InvokeSet("SetFixedFanStatus", 0);
            InvokeSet("SetStepFanStatus", 0);
            InvokeSet("SetAutoFanStatus", 1);
            InvokeSet("SetFixedFanSpeed", 0);
            InvokeSet("SetGPUFanDuty", 0);
            InvokeSet("SetWhisperMode", 0);
            Log("Restored Auto Fan Status (Hardware EC Control).");
        }

        public uint GetCpuRpm() => InvokeGet("getRpm1");
        public uint GetGpuRpm() => InvokeGet("getRpm2");
        public uint GetCpuTemp() => InvokeGet("getCpuTemp");
        public uint GetGpuTemp() => InvokeGet("getGpuTemp1");

        private void InvokeSet(string methodName, byte val)
        {
            try
            {
                if (_mcSet == null) return;
                ManagementBaseObject inParams = _mcSet.GetMethodParameters(methodName);
                inParams["Data"] = val;
                foreach (ManagementObject inst in _mcSet.GetInstances())
                {
                    inst.InvokeMethod(methodName, inParams, null);
                }
            }
            catch (Exception ex)
            {
                Log($"InvokeSet {methodName}({val}) failed: {ex.Message}");
            }
        }

        private uint InvokeGet(string methodName)
        {
            try
            {
                if (_mcGet == null) return 0;
                foreach (ManagementObject inst in _mcGet.GetInstances())
                {
                    ManagementBaseObject outParams = inst.InvokeMethod(methodName, null, null);
                    if (outParams != null && outParams["Data"] != null)
                    {
                        return Convert.ToUInt32(outParams["Data"]);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"InvokeGet {methodName} failed: {ex.Message}");
            }
            return 0;
        }

        public void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
            try
            {
                File.AppendAllText(DebugLogPath, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
