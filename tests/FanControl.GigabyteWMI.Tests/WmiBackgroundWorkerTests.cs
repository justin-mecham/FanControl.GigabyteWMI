using System;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Background;
using FanControl.GigabyteWMI.Tests.Mocks;

namespace FanControl.GigabyteWMI.Tests
{
    public class WmiBackgroundWorkerTests
    {
        [Fact]
        public void InitialState_ShouldBeAutoModeWithDefaultTelemetry()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            worker.CurrentMode.Should().Be(0);
            worker.LatestCpuRpm.Should().Be(0);
            worker.LatestGpuRpm.Should().Be(0);
            worker.LatestCpuTemp.Should().Be(45);
            worker.LatestGpuTemp.Should().Be(40);
        }

        [Fact]
        public void SettingModerateSpeed_ShouldEnterAdaptiveModeAndApplySpeeds()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            // Set CPU to 50% and GPU to 40%
            worker.SetTargetDuty(50f, isGpu: false);
            worker.SetTargetDuty(40f, isGpu: true);
            worker.ProcessSingleTick();

            mockWmi.LastAppliedCpuRegSpeed.Should().Be(148); // DutyTable[8]
            mockWmi.LastAppliedGpuDuty.Should().Be(40);
            worker.CurrentMode.Should().Be(2);
        }

        [Fact]
        public void Setting100Percent_ShouldEngageTurboMode()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            worker.SetTargetDuty(100f, isGpu: false);
            worker.SetTargetDuty(100f, isGpu: true);
            worker.ProcessSingleTick();

            mockWmi.IsTurboModeApplied.Should().BeTrue();
            worker.CurrentMode.Should().Be(3);
        }

        [Fact]
        public void LoweringFromTurbo_ShouldExitTurboModeAndReleaseLatch()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            // First enter Turbo
            worker.SetTargetDuty(100f, isGpu: false);
            worker.SetTargetDuty(100f, isGpu: true);
            worker.ProcessSingleTick();
            mockWmi.IsTurboModeApplied.Should().BeTrue();

            // Lower to 30%
            worker.SetTargetDuty(30f, isGpu: false);
            worker.SetTargetDuty(30f, isGpu: true);
            worker.ProcessSingleTick();

            mockWmi.IsTurboModeExited.Should().BeTrue();
            worker.CurrentMode.Should().Be(2);
            mockWmi.LastAppliedGpuDuty.Should().Be(30);
        }

        [Fact]
        public void SettingLowDuty_ShouldEngageSilentEcoMode()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            worker.SetTargetDuty(0f, isGpu: false);
            worker.SetTargetDuty(0f, isGpu: true);
            worker.ProcessSingleTick();

            mockWmi.IsSilentEcoModeApplied.Should().BeTrue();
            worker.CurrentMode.Should().Be(1);
        }

        [Fact]
        public void RequestResetToAuto_ShouldRestoreAutoMode()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);

            worker.SetTargetDuty(50f, isGpu: false);
            worker.ProcessSingleTick();
            worker.CurrentMode.Should().Be(2);

            worker.RequestResetToAuto();
            worker.ProcessSingleTick();

            mockWmi.IsAutoModeRestored.Should().BeTrue();
            worker.CurrentMode.Should().Be(0);
        }

        [Fact]
        public void TelemetryPolling_ShouldUpdatePropertiesOnTick()
        {
            var mockWmi = new MockGigabyteWmiService
            {
                MockCpuRpm = 4500,
                MockGpuRpm = 4200,
                MockCpuTemp = 65,
                MockGpuTemp = 58
            };
            var worker = new WmiBackgroundWorker(mockWmi);

            worker.ProcessSingleTick();

            worker.LatestCpuRpm.Should().Be(4500);
            worker.LatestGpuRpm.Should().Be(4200);
            worker.LatestCpuTemp.Should().Be(65);
            worker.LatestGpuTemp.Should().Be(58);
        }
    }
}
