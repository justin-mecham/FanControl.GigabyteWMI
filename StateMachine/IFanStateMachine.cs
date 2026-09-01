using FanControl.GigabyteWMI.Core;

namespace FanControl.GigabyteWMI.StateMachine
{
    /// <summary>
    /// Contract for the hardware fan state machine managing operational modes and ACPI transitions.
    /// </summary>
    public interface IFanStateMachine
    {
        FanMode CurrentMode { get; }
        byte AppliedCpuDuty { get; }
        byte AppliedGpuDuty { get; }

        void ProcessTarget(FanTarget target);
        void Heartbeat();
        void ResetToAuto();
    }
}
