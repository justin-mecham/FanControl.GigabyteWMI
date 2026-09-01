using System;
using System.Collections.Generic;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.Tests.Mocks
{
    public class MockGigabyteWmiService : IGigabyteWmiService
    {
        public bool IsConnected { get; set; } = true;
        public bool IsManualModeEntered { get; private set; } = false;
        public bool IsTurboModeApplied { get; private set; } = false;
        public bool IsTurboModeExited { get; private set; } = false;
        public bool IsSilentEcoModeApplied { get; private set; } = false;
        public bool IsAutoModeRestored { get; private set; } = false;

        public byte LastAppliedCpuRegSpeed { get; private set; } = 0;
        public byte LastAppliedGpuDuty { get; private set; } = 0;

        public uint MockCpuRpm { get; set; } = 2200;
        public uint MockGpuRpm { get; set; } = 2400;
        public uint MockCpuTemp { get; set; } = 48;
        public uint MockGpuTemp { get; set; } = 42;

        public List<string> CallLog { get; } = new List<string>();

        public bool EnsureConnected()
        {
            CallLog.Add("EnsureConnected");
            return IsConnected;
        }

        public void EnterManualMode()
        {
            IsManualModeEntered = true;
            IsAutoModeRestored = false;
            IsSilentEcoModeApplied = false;
            CallLog.Add("EnterManualMode");
        }

        public void SetFanSpeeds(byte cpuRegSpeed, byte gpuDuty)
        {
            LastAppliedCpuRegSpeed = cpuRegSpeed;
            LastAppliedGpuDuty = gpuDuty;
            CallLog.Add($"SetFanSpeeds(cpu={cpuRegSpeed}, gpu={gpuDuty})");
        }

        public void ApplyTurboMode()
        {
            IsTurboModeApplied = true;
            IsTurboModeExited = false;
            CallLog.Add("ApplyTurboMode");
        }

        public void ExitTurboMode()
        {
            IsTurboModeExited = true;
            IsTurboModeApplied = false;
            CallLog.Add("ExitTurboMode");
        }

        public void ApplySilentEcoMode()
        {
            IsSilentEcoModeApplied = true;
            IsAutoModeRestored = false;
            CallLog.Add("ApplySilentEcoMode");
        }

        public void RestoreAutoMode()
        {
            IsAutoModeRestored = true;
            IsManualModeEntered = false;
            IsTurboModeApplied = false;
            IsSilentEcoModeApplied = false;
            CallLog.Add("RestoreAutoMode");
        }

        public uint GetCpuRpm()
        {
            CallLog.Add("GetCpuRpm");
            return MockCpuRpm;
        }

        public uint GetGpuRpm()
        {
            CallLog.Add("GetGpuRpm");
            return MockGpuRpm;
        }

        public uint GetCpuTemp()
        {
            CallLog.Add("GetCpuTemp");
            return MockCpuTemp;
        }

        public uint GetGpuTemp()
        {
            CallLog.Add("GetGpuTemp");
            return MockGpuTemp;
        }

        public void Log(string msg)
        {
            CallLog.Add($"Log: {msg}");
        }
    }
}
