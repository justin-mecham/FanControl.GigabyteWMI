using System;
using System.IO;
using System.Management;
using System.Media;
using System.Windows.Forms;
using System.Drawing;

namespace GigabyteFanToggle
{
    /// <summary>
    /// Lightweight standalone system tray utility to instantly toggle Gigabyte laptop fans
    /// between Whisper Silent Mode (1600 RPM) and 100% MAX Panic/Turbo Mode (7500 RPM) via WMI ACPI.
    /// </summary>
    class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                string stateDir = AppDomain.CurrentDomain.BaseDirectory;
                string stateFile = Path.Combine(stateDir, "fan_state.txt");
                string currentState = "Silent";

                if (File.Exists(stateFile))
                {
                    currentState = File.ReadAllText(stateFile).Trim();
                }

                ConnectionOptions co = new ConnectionOptions
                {
                    EnablePrivileges = true,
                    Impersonation = ImpersonationLevel.Impersonate
                };
                ManagementScope scope = new ManagementScope(@"root\WMI", co);
                scope.Connect();

                ManagementClass mcSet = new ManagementClass(scope, new ManagementPath("GB_WMIACPI_Set"), null);
                ManagementClass mcGet = new ManagementClass(scope, new ManagementPath("GB_WMIACPI_Get"), null);

                string title = "Gigabyte Fan Control";
                string msg;

                if (currentState == "Max")
                {
                    // Switch to True Silent / Whisper Eco Mode (~1600 RPM)
                    InvokeSet(mcSet, "SetTurboMode", 0);
                    InvokeSet(mcSet, "SetTppStatus", 0);
                    InvokeSet(mcSet, "SetFixedFanStatus", 0);
                    InvokeSet(mcSet, "SetStepFanStatus", 0);
                    InvokeSet(mcSet, "SetAutoFanStatus", 1);
                    InvokeSet(mcSet, "SetFixedFanSpeed", 0);
                    InvokeSet(mcSet, "SetGPUFanDuty", 0);
                    InvokeSet(mcSet, "SetWhisperMode", 1);

                    File.WriteAllText(stateFile, "Silent");
                    SystemSounds.Asterisk.Play();

                    uint cpuTemp = InvokeGet(mcGet, "getCpuTemp");
                    uint r1 = InvokeGet(mcGet, "getRpm1");
                    uint r2 = InvokeGet(mcGet, "getRpm2");
                    msg = string.Format("Mode: SILENT / Whisper Eco\nCPU: {0}°C  |  CPU: {1} RPM | GPU: {2} RPM", cpuTemp, r1, r2);
                }
                else
                {
                    // Switch to 100% Full MAX Panic / Turbo Mode (7500 RPM)
                    InvokeSet(mcSet, "SetAutoFanStatus", 0);
                    InvokeSet(mcSet, "SetWhisperMode", 0);
                    InvokeSet(mcSet, "SetStepFanStatus", 1);
                    InvokeSet(mcSet, "SetFixedFanStatus", 1);
                    InvokeSet(mcSet, "SetFixedFanSpeed", 229);
                    InvokeSet(mcSet, "SetGPUFanDuty", 100);
                    InvokeSet(mcSet, "SetTurboMode", 1);
                    InvokeSet(mcSet, "SetTppStatus", 1);

                    File.WriteAllText(stateFile, "Max");
                    SystemSounds.Exclamation.Play();

                    uint cpuTemp = InvokeGet(mcGet, "getCpuTemp");
                    uint r1 = InvokeGet(mcGet, "getRpm1");
                    uint r2 = InvokeGet(mcGet, "getRpm2");
                    msg = string.Format("Mode: 100% MAX Panic Turbo (7500 RPM)\nCPU: {0}°C  |  CPU: {1} RPM | GPU: {2} RPM", cpuTemp, r1, r2);
                }

                using (NotifyIcon icon = new NotifyIcon())
                {
                    icon.Icon = SystemIcons.Shield;
                    icon.Visible = true;
                    icon.BalloonTipTitle = title;
                    icon.BalloonTipText = msg;
                    icon.ShowBalloonTip(2000);
                    System.Threading.Thread.Sleep(2000);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error toggling fans: " + ex.Message, "Fan Control Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void InvokeSet(ManagementClass mc, string methodName, byte val)
        {
            try
            {
                ManagementBaseObject inParams = mc.GetMethodParameters(methodName);
                inParams["Data"] = val;
                foreach (ManagementObject inst in mc.GetInstances())
                {
                    inst.InvokeMethod(methodName, inParams, null);
                }
            }
            catch { }
        }

        static uint InvokeGet(ManagementClass mc, string methodName)
        {
            try
            {
                foreach (ManagementObject inst in mc.GetInstances())
                {
                    ManagementBaseObject outParams = inst.InvokeMethod(methodName, null, null);
                    if (outParams != null && outParams["Data"] != null)
                    {
                        return Convert.ToUInt32(outParams["Data"]);
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}
