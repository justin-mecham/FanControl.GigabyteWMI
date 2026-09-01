using System;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.StateMachine
{
    /// <summary>
    /// Encapsulates the ACPI hardware state machine for Gigabyte laptop Embedded Controllers.
    ///
    /// Live-hardware testing on the GAMING A16 CVH (BIOS FB05) established that this EC ignores
    /// every proportional-speed register (SetFixedFanSpeed, SetFanIndexValue table, SetFanSpeed:
    /// writes read back correctly or error, but never change fan behavior). The firmware only
    /// implements DISCRETE modes, so the requested duty is quantized onto a 4-tier ladder:
    ///
    ///   duty &lt; 10   -> WhisperEco  (Auto=1 + Whisper=1,        ~1600 RPM)
    ///   10..79      -> StepCurve   (Auto=0, Fixed=0, Step=1,   ~52% duty curve)
    ///   80..97      -> FixedBoost  (Auto=0, Fixed=1, Step=1,   ~87% duty)
    ///   &gt;= 98       -> MaxTurbo    (FixedBoost + TPP + Turbo,  100%, ~7500 RPM)
    ///
    /// The speed registers are still written on the boost tiers so the plugin degrades
    /// gracefully on other Gigabyte models whose EC may honor them.
    /// </summary>
    public class FanStateMachine : IFanStateMachine
    {
        // Hysteresis bands prevent mode thrash while FanControl's response-time smoothing
        // walks the commanded duty through a tier boundary (e.g. 100 -> 98 -> 95 -> 92 -> 100).
        private const byte TurboEnterAt = 98;
        private const byte TurboExitBelow = 93;
        private const byte BoostEnterAt = 80;
        private const byte BoostExitBelow = 75;
        private const byte WhisperEnterBelow = 10;
        private const byte WhisperExitAt = 15;

        private readonly IGigabyteWmiDriver _driver;
        private bool _autoApplied;

        public FanMode CurrentMode { get; private set; } = FanMode.Auto;
        public byte AppliedCpuDuty { get; private set; } = FanTarget.Unset;
        public byte AppliedGpuDuty { get; private set; } = FanTarget.Unset;

        public FanStateMachine(IGigabyteWmiDriver driver)
        {
            _driver = driver;
        }

        public void ResetToAuto()
        {
            // Idempotent: the worker calls this every tick while no target is set.
            if (CurrentMode == FanMode.Auto && _autoApplied)
                return;

            EnterAutoMode();
        }

        public void ProcessTarget(FanTarget target)
        {
            if (target.IsResetRequested ||
                (target.CpuDuty == FanTarget.Unset && target.GpuDuty == FanTarget.Unset))
            {
                ResetToAuto();
                return;
            }

            byte effectiveCpu = target.EffectiveCpuDuty;
            byte effectiveGpu = target.EffectiveGpuDuty;
            FanMode desired = SelectTier(Math.Max(effectiveCpu, effectiveGpu));

            if (desired != CurrentMode)
            {
                switch (desired)
                {
                    case FanMode.MaxTurbo:   EnterMaxTurbo();   break;
                    case FanMode.FixedBoost: EnterFixedBoost(); break;
                    case FanMode.StepCurve:  EnterStepCurve();  break;
                    case FanMode.WhisperEco: EnterWhisperEco(); break;
                }
            }

            // Track requested duties and keep writing the speed registers on the boost tier
            // (inert on the A16 CVH, meaningful on ECs that implement them).
            if (CurrentMode == FanMode.FixedBoost &&
                (effectiveCpu != AppliedCpuDuty || effectiveGpu != AppliedGpuDuty))
            {
                ApplyBoostSpeeds(effectiveCpu, effectiveGpu);
            }
            else if (CurrentMode == FanMode.StepCurve)
            {
                AppliedCpuDuty = effectiveCpu;
                AppliedGpuDuty = effectiveGpu;
            }
        }

        /// <summary>
        /// Quantizes a duty percentage onto the discrete hardware tier ladder,
        /// with hysteresis relative to the current mode at every boundary.
        /// </summary>
        private FanMode SelectTier(byte duty)
        {
            byte turboThreshold = CurrentMode == FanMode.MaxTurbo ? TurboExitBelow : TurboEnterAt;
            if (duty >= turboThreshold)
                return FanMode.MaxTurbo;

            byte whisperThreshold = CurrentMode == FanMode.WhisperEco ? WhisperExitAt : WhisperEnterBelow;
            if (duty < whisperThreshold)
                return FanMode.WhisperEco;

            byte boostThreshold = CurrentMode == FanMode.FixedBoost || CurrentMode == FanMode.MaxTurbo
                ? BoostExitBelow
                : BoostEnterAt;
            return duty >= boostThreshold ? FanMode.FixedBoost : FanMode.StepCurve;
        }

        /// <summary>
        /// Periodically re-asserts the current non-auto mode's full hardware sequence.
        /// The EC loses its status bits on hibernate-resume (GCC's own BoostResume handler
        /// re-applies the fan mode for the same reason).
        /// </summary>
        public void Heartbeat()
        {
            switch (CurrentMode)
            {
                case FanMode.MaxTurbo:
                    WriteTurboSequence();
                    break;
                case FanMode.FixedBoost:
                    WriteFixedBoostSequence();
                    if (AppliedCpuDuty != FanTarget.Unset && AppliedGpuDuty != FanTarget.Unset)
                    {
                        _driver.SetMethod("SetFixedFanSpeed", DutyTable.ToRegisterSpeed(AppliedCpuDuty));
                        _driver.SetMethod("SetGPUFanDuty", AppliedGpuDuty);
                    }
                    break;
                case FanMode.StepCurve:
                    WriteStepCurveSequence();
                    break;
                case FanMode.WhisperEco:
                    WriteWhisperSequence();
                    break;
            }
        }

        private void EnterAutoMode()
        {
            _driver.SetMethod("SetTurboMode", 0);
            _driver.SetMethod("SetTppStatus", 0);
            _driver.SetMethod("SetWhisperMode", 0);
            _driver.SetMethod("SetFixedFanStatus", 0);
            _driver.SetMethod("SetStepFanStatus", 0);
            _driver.SetMethod("SetAutoFanStatus", 1);
            _driver.SetMethod("SetFixedFanSpeed", 0);
            _driver.SetMethod("SetGPUFanDuty", 0);

            CurrentMode = FanMode.Auto;
            _autoApplied = true;
            AppliedCpuDuty = FanTarget.Unset;
            AppliedGpuDuty = FanTarget.Unset;
            _driver.Log("State Transition -> Auto Mode (Hardware EC Control).");
        }

        private void EnterWhisperEco()
        {
            WriteWhisperSequence();

            CurrentMode = FanMode.WhisperEco;
            AppliedCpuDuty = 0;
            AppliedGpuDuty = 0;
            _driver.Log("State Transition -> Whisper Eco (~1600 RPM).");
        }

        private void WriteWhisperSequence()
        {
            _driver.SetMethod("SetTurboMode", 0);
            _driver.SetMethod("SetTppStatus", 0);
            _driver.SetMethod("SetFixedFanStatus", 0);
            _driver.SetMethod("SetStepFanStatus", 0);
            _driver.SetMethod("SetAutoFanStatus", 1);
            _driver.SetMethod("SetFixedFanSpeed", 0);
            _driver.SetMethod("SetGPUFanDuty", 0);
            _driver.SetMethod("SetWhisperMode", 1);
        }

        // GCC applyFANprofile custom branch: the EC's built-in moderate step curve
        // (~52% duty at 76C on the A16 CVH; verified below stock auto at the same temp).
        private void EnterStepCurve()
        {
            WriteStepCurveSequence();

            CurrentMode = FanMode.StepCurve;
            _driver.Log("State Transition -> Step Curve (moderate hardware curve, ~52% duty).");
        }

        private void WriteStepCurveSequence()
        {
            _driver.SetMethod("SetTurboMode", 0);
            _driver.SetMethod("SetTppStatus", 0);
            _driver.SetMethod("SetWhisperMode", 0);
            _driver.SetMethod("SetFixedFanStatus", 0);
            _driver.SetMethod("SetStepFanStatus", 1);
            _driver.SetMethod("SetAutoFanStatus", 0);
        }

        // Fixed+Step engaged: the EC runs its aggressive ~87% profile regardless of the
        // speed registers (which are still written for other-model compatibility).
        private void EnterFixedBoost()
        {
            WriteFixedBoostSequence();

            CurrentMode = FanMode.FixedBoost;
            AppliedCpuDuty = FanTarget.Unset;
            AppliedGpuDuty = FanTarget.Unset;
            _driver.Log("State Transition -> Fixed Boost (~87% duty).");
        }

        private void WriteFixedBoostSequence()
        {
            _driver.SetMethod("SetTurboMode", 0);
            _driver.SetMethod("SetTppStatus", 0);
            _driver.SetMethod("SetWhisperMode", 0);
            _driver.SetMethod("SetFixedFanStatus", 1);
            _driver.SetMethod("SetStepFanStatus", 1);
            _driver.SetMethod("SetAutoFanStatus", 0);
        }

        private void ApplyBoostSpeeds(byte cpuDuty, byte gpuDuty)
        {
            byte cpuRegSpeed = DutyTable.ToRegisterSpeed(cpuDuty);
            _driver.SetMethod("SetFixedFanSpeed", cpuRegSpeed);
            _driver.SetMethod("SetGPUFanDuty", gpuDuty);

            AppliedCpuDuty = cpuDuty;
            AppliedGpuDuty = gpuDuty;
            _driver.Log($"Dispatched Boost Speeds -> CPU Reg: {cpuRegSpeed} ({cpuDuty}%), GPU Duty: {gpuDuty}%");
        }

        // Mirrors GCC ucNotebook FanControl.SetFanMax call order, plus SetTurboMode.
        private void EnterMaxTurbo()
        {
            WriteTurboSequence();

            CurrentMode = FanMode.MaxTurbo;
            AppliedCpuDuty = 100;
            AppliedGpuDuty = 100;
            _driver.Log("State Transition -> 100% MAX Turbo (TPP Active, ~7500 RPM).");
        }

        private void WriteTurboSequence()
        {
            _driver.SetMethod("SetAutoFanStatus", 0);
            _driver.SetMethod("SetWhisperMode", 0);
            _driver.SetMethod("SetFixedFanSpeed", 229);
            _driver.SetMethod("SetGPUFanDuty", 100);
            _driver.SetMethod("SetStepFanStatus", 1);
            _driver.SetMethod("SetFixedFanStatus", 1);
            _driver.SetMethod("SetTppStatus", 1);
            _driver.SetMethod("SetTurboMode", 1);
        }
    }
}
