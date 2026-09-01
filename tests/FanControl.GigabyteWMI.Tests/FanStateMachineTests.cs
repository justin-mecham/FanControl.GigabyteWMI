using System;
using System.Linq;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.StateMachine;
using FanControl.GigabyteWMI.Tests.Mocks;

namespace FanControl.GigabyteWMI.Tests
{
    public class FanStateMachineTests
    {
        [Fact]
        public void InitialState_ShouldBeAuto()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.CurrentMode.Should().Be(FanMode.Auto);
            sm.AppliedCpuDuty.Should().Be(FanTarget.Unset);
            sm.AppliedGpuDuty.Should().Be(FanTarget.Unset);
        }

        [Fact]
        public void SettingSilentDuty_ShouldTransitionToWhisperEco()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(5, 5));

            sm.CurrentMode.Should().Be(FanMode.WhisperEco);
            mockDriver.LastSetMethods["SetWhisperMode"].Should().Be(1);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
        }

        [Fact]
        public void MidRangeDuty_ShouldEngageStepCurve()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            // The A16 CVH EC ignores proportional speed registers; 10-79% maps to the
            // built-in moderate step curve (Auto=0, Fixed=0, Step=1).
            sm.ProcessTarget(new FanTarget(50, 60));

            sm.CurrentMode.Should().Be(FanMode.StepCurve);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetWhisperMode"].Should().Be(0);
            mockDriver.LastSetMethods["SetTppStatus"].Should().Be(0);
            sm.AppliedCpuDuty.Should().Be(50);
            sm.AppliedGpuDuty.Should().Be(60);
        }

        [Fact]
        public void HighDuty_ShouldEngageFixedBoostWithSpeedWrites()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            // 85% -> FixedBoost (Fixed=1, Step=1). Speed registers are written for
            // other-model compatibility even though this EC ignores them.
            sm.ProcessTarget(new FanTarget(85, 85));

            sm.CurrentMode.Should().Be(FanMode.FixedBoost);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetFixedFanSpeed"].Should().Be(206); // DutyTable @ 85%
            mockDriver.LastSetMethods["SetGPUFanDuty"].Should().Be(85);
        }

        [Fact]
        public void SettingMaxDuty_ShouldTransitionToMaxTurboWithTpp()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(100, 100));

            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);
            mockDriver.LastSetMethods["SetFixedFanSpeed"].Should().Be(229);
            mockDriver.LastSetMethods["SetGPUFanDuty"].Should().Be(100);
            mockDriver.LastSetMethods["SetTurboMode"].Should().Be(1);
            mockDriver.LastSetMethods["SetTppStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
        }

        [Fact]
        public void TierSelection_ShouldFollowNeediestFan()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            // The EC drives both fans together, so the higher demand wins the tier.
            sm.ProcessTarget(new FanTarget(100, 50));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);

            sm.ProcessTarget(new FanTarget(30, 85));
            sm.CurrentMode.Should().Be(FanMode.FixedBoost);

            sm.ProcessTarget(new FanTarget(5, 50));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);
        }

        [Fact]
        public void SingleCpuSlider_ShouldMirrorAndReachAllTiers()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(100, FanTarget.Unset));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);

            sm.ProcessTarget(new FanTarget(50, FanTarget.Unset));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);
            sm.AppliedGpuDuty.Should().Be(50); // mirrored

            sm.ProcessTarget(new FanTarget(5, FanTarget.Unset));
            sm.CurrentMode.Should().Be(FanMode.WhisperEco);
        }

        [Fact]
        public void TurboHysteresis_ShouldHoldThroughSmoothingDips()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(100, 100));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);

            // FanControl's response-time smoothing walks the value down before settling;
            // a dip to 95 must NOT bounce out of turbo (this thrashed every 1-2s in the field).
            sm.ProcessTarget(new FanTarget(95, 95));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);

            sm.ProcessTarget(new FanTarget(93, 93));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);

            sm.ProcessTarget(new FanTarget(92, 92));
            sm.CurrentMode.Should().Be(FanMode.FixedBoost);
        }

        [Fact]
        public void TurboHysteresis_ShouldNotEnterBelowEnterThreshold()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(97, 97));
            sm.CurrentMode.Should().Be(FanMode.FixedBoost);

            sm.ProcessTarget(new FanTarget(98, 98));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);
        }

        [Fact]
        public void BoostHysteresis_ShouldHoldBetweenExitAndEnter()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(79, 79));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);

            sm.ProcessTarget(new FanTarget(80, 80));
            sm.CurrentMode.Should().Be(FanMode.FixedBoost);

            // 76 is below the 80 entry bound but above the 75 exit bound: hold boost.
            sm.ProcessTarget(new FanTarget(76, 76));
            sm.CurrentMode.Should().Be(FanMode.FixedBoost);

            sm.ProcessTarget(new FanTarget(74, 74));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);
        }

        [Fact]
        public void WhisperHysteresis_ShouldHoldWhisperUntilExitThreshold()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(5, 5));
            sm.CurrentMode.Should().Be(FanMode.WhisperEco);

            // 12% is above the 10% entry bound but below the 15% exit bound: stay in whisper.
            sm.ProcessTarget(new FanTarget(12, 12));
            sm.CurrentMode.Should().Be(FanMode.WhisperEco);

            sm.ProcessTarget(new FanTarget(15, 15));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);
        }

        [Fact]
        public void Heartbeat_InEachMode_ShouldReassertItsFullSequence()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            // StepCurve: mode bits re-asserted (EC drops them on hibernate-resume)
            sm.ProcessTarget(new FanTarget(50, 50));
            mockDriver.SetMethodHistory.Clear();
            sm.Heartbeat();
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetStepFanStatus" && x.Value == 1);
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetAutoFanStatus" && x.Value == 0);

            // FixedBoost: mode bits plus last speeds
            sm.ProcessTarget(new FanTarget(85, 85));
            mockDriver.SetMethodHistory.Clear();
            sm.Heartbeat();
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetFixedFanStatus" && x.Value == 1);
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetFixedFanSpeed" && x.Value == 206);
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetGPUFanDuty" && x.Value == 85);

            // MaxTurbo: heartbeat stays silent - Overdrive's kick cycle re-writes the
            // mode bits every <=3s itself, and a heartbeat rewrite would cut a droop short.
            sm.ProcessTarget(new FanTarget(100, 100));
            mockDriver.SetMethodHistory.Clear();
            sm.Heartbeat();
            mockDriver.SetMethodHistory.Should().BeEmpty();

            // Whisper
            sm.ProcessTarget(new FanTarget(5, 5));
            mockDriver.SetMethodHistory.Clear();
            sm.Heartbeat();
            mockDriver.SetMethodHistory.Should().Contain(x => x.Method == "SetWhisperMode" && x.Value == 1);
        }

        [Fact]
        public void Heartbeat_InAutoMode_ShouldNotTouchHardware()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(FanTarget.Reset);
            mockDriver.SetMethodHistory.Clear();

            sm.Heartbeat();

            mockDriver.SetMethodHistory.Should().BeEmpty();
        }

        [Fact]
        public void UnsetTarget_ShouldApplyAutoModeExactlyOnce()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            // First unset tick stamps the hardware into Auto once (failsafe for an EC
            // left in manual by a previous crash)...
            sm.ProcessTarget(new FanTarget(FanTarget.Unset, FanTarget.Unset));
            sm.CurrentMode.Should().Be(FanMode.Auto);
            int writesAfterFirstTick = mockDriver.SetMethodHistory.Count;
            writesAfterFirstTick.Should().BeGreaterThan(0);

            // ...and every subsequent unset tick is a no-op.
            sm.ProcessTarget(new FanTarget(FanTarget.Unset, FanTarget.Unset));
            sm.ProcessTarget(FanTarget.Reset);
            mockDriver.SetMethodHistory.Count.Should().Be(writesAfterFirstTick);
        }

        [Fact]
        public void RepeatedSameTarget_ShouldNotRewriteHardware()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(50, 50));
            int writesAfterFirst = mockDriver.SetMethodHistory.Count;

            sm.ProcessTarget(new FanTarget(50, 50));
            sm.ProcessTarget(new FanTarget(55, 55)); // same tier -> no writes either
            mockDriver.SetMethodHistory.Count.Should().Be(writesAfterFirst);
        }

        [Fact]
        public void ResetToAuto_ShouldRestoreHardwareEcControl()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(50, 50));
            sm.CurrentMode.Should().Be(FanMode.StepCurve);

            sm.ResetToAuto();
            sm.CurrentMode.Should().Be(FanMode.Auto);

            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetWhisperMode"].Should().Be(0);
            mockDriver.LastSetMethods["SetTurboMode"].Should().Be(0);
            mockDriver.LastSetMethods["SetTppStatus"].Should().Be(0);
        }

        [Fact]
        public void AllModeTransitions_ShouldNeverCallUnimplementedWmiMethods()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var sm = new FanStateMachine(mockDriver);

            sm.ProcessTarget(new FanTarget(50, 50));
            sm.ProcessTarget(new FanTarget(85, 85));
            sm.ProcessTarget(new FanTarget(100, 100));
            sm.Heartbeat();
            sm.ProcessTarget(new FanTarget(5, 5));
            sm.ResetToAuto();

            // Verified inert or unimplemented on the A16 CVH firmware:
            mockDriver.SetMethodHistory.Should().NotContain(x => x.Method == "SetSmartTurbo");
            mockDriver.SetMethodHistory.Should().NotContain(x => x.Method == "SetFanAdjustStatus");
            mockDriver.SetMethodHistory.Should().NotContain(x => x.Method == "SetFanSpeed");
            mockDriver.SetMethodHistory.Should().NotContain(x => x.Method == "SetFanIndexValue");
        }
    
        [Fact]
        public void Overdrive_ShouldDropAdjfAfterBoostPhase()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var sm = new FanStateMachine(mockDriver, () => now);

            sm.ProcessTarget(new FanTarget(100, 100));
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);

            // Within the 2.5s boost phase: no droop yet.
            now = now.AddMilliseconds(2400);
            sm.ProcessTarget(new FanTarget(100, 100));
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);

            // Past the boost phase: ADJF drops (fans droop on the step curve).
            now = now.AddMilliseconds(200);
            sm.ProcessTarget(new FanTarget(100, 100));
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            sm.CurrentMode.Should().Be(FanMode.MaxTurbo);
        }

        [Fact]
        public void Overdrive_ShouldRefireKickAfterDropPhase()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var sm = new FanStateMachine(mockDriver, () => now);

            sm.ProcessTarget(new FanTarget(100, 100));
            now = now.AddMilliseconds(2600);
            sm.ProcessTarget(new FanTarget(100, 100)); // enters droop
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);

            // 750ms droop elapses: boost re-asserts ADJF (and Step/Auto invariants).
            now = now.AddMilliseconds(800);
            sm.ProcessTarget(new FanTarget(100, 100));
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
        }

        [Fact]
        public void Overdrive_LeavingMaxTurbo_ShouldStopCycling()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var sm = new FanStateMachine(mockDriver, () => now);

            sm.ProcessTarget(new FanTarget(100, 100));
            now = now.AddMilliseconds(2600);
            sm.ProcessTarget(new FanTarget(100, 100)); // droop phase, ADJF=0
            sm.ProcessTarget(new FanTarget(85, 85));   // exit turbo -> FixedBoost

            sm.CurrentMode.Should().Be(FanMode.FixedBoost);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(1);
        }
    }
}
