using System;

namespace FanControl.GigabyteWMI.Hardware
{
    /// <summary>
    /// Contract for Gigabyte laptop ACPI WMI communication.
    /// Allows seamless hardware mocking and isolated unit testing without live WMI hardware.
    /// </summary>
    public interface IGigabyteWmiService
    {
        bool EnsureConnected();
        void EnterManualMode();
        void SetFanSpeeds(byte cpuRegSpeed, byte gpuDuty);
        void ApplyTurboMode();
        void ExitTurboMode();
        void ApplySilentEcoMode();
        void RestoreAutoMode();
        uint GetCpuRpm();
        uint GetGpuRpm();
        uint GetCpuTemp();
        uint GetGpuTemp();
        void Log(string msg);
    }
}
