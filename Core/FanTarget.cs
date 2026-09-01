using System;

namespace FanControl.GigabyteWMI.Core
{
    /// <summary>
    /// Encapsulates desired fan duty target states for CPU and GPU with dirty tracking.
    /// </summary>
    public struct FanTarget : IEquatable<FanTarget>
    {
        public const byte Unset = 255;

        public byte CpuDuty { get; }
        public byte GpuDuty { get; }
        public bool IsResetRequested { get; }

        public FanTarget(byte cpuDuty, byte gpuDuty, bool isResetRequested = false)
        {
            CpuDuty = cpuDuty;
            GpuDuty = gpuDuty;
            IsResetRequested = isResetRequested;
        }

        public static FanTarget Reset => new FanTarget(Unset, Unset, true);

        public byte MaxActiveDuty
        {
            get
            {
                byte c = CpuDuty != Unset ? CpuDuty : (GpuDuty != Unset ? GpuDuty : (byte)0);
                byte g = GpuDuty != Unset ? GpuDuty : (CpuDuty != Unset ? CpuDuty : (byte)0);
                return Math.Max(c, g);
            }
        }

        public byte EffectiveCpuDuty => CpuDuty != Unset ? CpuDuty : (GpuDuty != Unset ? GpuDuty : (byte)0);
        public byte EffectiveGpuDuty => GpuDuty != Unset ? GpuDuty : (CpuDuty != Unset ? CpuDuty : (byte)0);

        public bool Equals(FanTarget other)
        {
            return CpuDuty == other.CpuDuty &&
                   GpuDuty == other.GpuDuty &&
                   IsResetRequested == other.IsResetRequested;
        }

        public override bool Equals(object? obj) => obj is FanTarget other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + CpuDuty.GetHashCode();
                hash = hash * 31 + GpuDuty.GetHashCode();
                hash = hash * 31 + IsResetRequested.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(FanTarget left, FanTarget right) => left.Equals(right);
        public static bool operator !=(FanTarget left, FanTarget right) => !left.Equals(right);
    }
}
