using System;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.Background;
using FanControl.GigabyteWMI.StateMachine;
using FanControl.GigabyteWMI.Tests.Mocks;

namespace FanControl.GigabyteWMI.Tests
{
    public class WmiBackgroundWorkerTests
    {
        [Fact]
        public void InitialState_ShouldBeAutoModeWithDefaultTelemetry()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            worker.CurrentMode.Should().Be(FanMode.Auto);
            worker.LatestCpuRpm.Should().Be(0);
            worker.LatestGpuRpm.Should().Be(0);
            worker.LatestCpuTemp.Should().Be(45);
            worker.LatestGpuTemp.Should().Be(40);
        }

        [Fact]
        public void SettingModerateSpeed_ShouldEnterStepCurveMode()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            // Set CPU to 50% and GPU to 40% -> quantizes to the StepCurve tier
            worker.SetTargetDuty(50f, isGpu: false);
            worker.SetTargetDuty(40f, isGpu: true);
            worker.ProcessSingleTick();

            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
            worker.CurrentMode.Should().Be(FanMode.StepCurve);
        }

        [Fact]
        public void Setting100Percent_ShouldEngageTurboMode()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            worker.SetTargetDuty(100f, isGpu: false);
            worker.SetTargetDuty(100f, isGpu: true);
            worker.ProcessSingleTick();

            mockDriver.LastSetMethods["SetTurboMode"].Should().Be(1);
            mockDriver.LastSetMethods["SetTppStatus"].Should().Be(1);
            worker.CurrentMode.Should().Be(FanMode.MaxTurbo);
        }

        [Fact]
        public void LoweringFromTurbo_ShouldExitTurboModeAndReleaseLatch()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            // First enter Turbo
            worker.SetTargetDuty(100f, isGpu: false);
            worker.SetTargetDuty(100f, isGpu: true);
            worker.ProcessSingleTick();
            worker.CurrentMode.Should().Be(FanMode.MaxTurbo);

            // Lower to 30% -> StepCurve tier, turbo latch released
            worker.SetTargetDuty(30f, isGpu: false);
            worker.SetTargetDuty(30f, isGpu: true);
            worker.ProcessSingleTick();

            worker.CurrentMode.Should().Be(FanMode.StepCurve);
            mockDriver.LastSetMethods["SetTurboMode"].Should().Be(0);
            mockDriver.LastSetMethods["SetTppStatus"].Should().Be(0);
        }

        [Fact]
        public void SettingLowDuty_ShouldEngageSilentEcoMode()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            worker.SetTargetDuty(0f, isGpu: false);
            worker.SetTargetDuty(0f, isGpu: true);
            worker.ProcessSingleTick();

            mockDriver.LastSetMethods["SetWhisperMode"].Should().Be(1);
            worker.CurrentMode.Should().Be(FanMode.WhisperEco);
        }

        [Fact]
        public void RequestResetToAuto_ShouldRestoreAutoMode()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            worker.SetTargetDuty(50f, isGpu: false);
            worker.ProcessSingleTick();
            worker.CurrentMode.Should().Be(FanMode.StepCurve);

            worker.RequestResetToAuto();
            worker.ProcessSingleTick();

            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(1);
            worker.CurrentMode.Should().Be(FanMode.Auto);
        }

        [Fact]
        public void PartialReset_ShouldStayInTierDrivenBySurvivingSlider()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);

            worker.SetTargetDuty(50f, isGpu: false);
            worker.SetTargetDuty(60f, isGpu: true);
            worker.ProcessSingleTick();
            worker.CurrentMode.Should().Be(FanMode.StepCurve);

            // Un-assigning only the GPU control must NOT drop to Auto; the surviving
            // CPU slider still owns the tier.
            worker.RequestResetToAuto(isGpu: true);
            worker.ProcessSingleTick();

            worker.CurrentMode.Should().Be(FanMode.StepCurve);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
        }

        [Fact]
        public void TelemetryPolling_ShouldUpdatePropertiesOnTick()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            mockDriver.GetMethodReturns["getRpm1"] = 4500;
            mockDriver.GetMethodReturns["getRpm2"] = 4200;
            mockDriver.GetMethodReturns["getCpuTemp"] = 65;
            mockDriver.GetMethodReturns["getGpuTemp1"] = 58;

            var worker = new WmiBackgroundWorker(mockDriver);
            worker.ProcessSingleTick();

            worker.LatestCpuRpm.Should().Be(4500);
            worker.LatestGpuRpm.Should().Be(4200);
            worker.LatestCpuTemp.Should().Be(65);
            worker.LatestGpuTemp.Should().Be(58);
        }
    }
}
