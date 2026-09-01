using System;

namespace FanControl.GigabyteWMI.Core
{
    /// <summary>
    /// Immutable snapshot of live hardware sensor readings.
    /// </summary>
    public class TelemetrySnapshot
    {
        public uint CpuRpm { get; }
        public uint GpuRpm { get; }
        public uint CpuTemp { get; }
        public uint GpuTemp { get; }
        public DateTime Timestamp { get; }

        public TelemetrySnapshot(uint cpuRpm, uint gpuRpm, uint cpuTemp, uint gpuTemp, DateTime timestamp)
        {
            CpuRpm = cpuRpm;
            GpuRpm = gpuRpm;
            CpuTemp = cpuTemp;
            GpuTemp = gpuTemp;
            Timestamp = timestamp;
        }

        public static TelemetrySnapshot Default => new TelemetrySnapshot(0, 0, 45, 40, DateTime.MinValue);
    }
}
