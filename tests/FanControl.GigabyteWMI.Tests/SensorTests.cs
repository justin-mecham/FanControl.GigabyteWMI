using System;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Hardware;
using FanControl.GigabyteWMI.Background;
using FanControl.GigabyteWMI.Sensors;
using FanControl.GigabyteWMI.Tests.Mocks;

namespace FanControl.GigabyteWMI.Tests
{
    public class SensorTests
    {
        [Fact]
        public void ControlSensor_Set_ShouldUpdateValueAndWorkerTarget()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);
            var sensor = new GigabyteControlSensor("gb_cpu_ctrl", "CPU Control", worker, isGpu: false);

            sensor.Set(45f);
            sensor.Value.Should().Be(45f);

            worker.ProcessSingleTick();
            mockWmi.LastAppliedCpuRegSpeed.Should().Be(DutyTable.ToRegisterSpeed(45f));
        }

        [Fact]
        public void ControlSensor_Reset_ShouldClearValueAndRequestAuto()
        {
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);
            var sensor = new GigabyteControlSensor("gb_cpu_ctrl", "CPU Control", worker, isGpu: false);

            sensor.Set(45f);
            worker.ProcessSingleTick();

            sensor.Reset();
            sensor.Value.Should().BeNull();

            worker.ProcessSingleTick();
            mockWmi.IsAutoModeRestored.Should().BeTrue();
        }

        [Fact]
        public void FanSensor_ValueAssignment_ShouldStoreRpm()
        {
            var sensor = new GigabyteFanSensor("gb_cpu_rpm", "CPU RPM");
            sensor.Value = 3500f;
            sensor.Value.Should().Be(3500f);
        }

        [Fact]
        public void TempSensor_SleepModeRetention_ShouldRetainActiveTemperatureWhenSleepReportsZero()
        {
            var mockWmi = new MockGigabyteWmiService { MockGpuTemp = 56 };
            var worker = new WmiBackgroundWorker(mockWmi);

            // Active 3D load: GPU wakes up and reports 56°C
            worker.ProcessSingleTick();
            worker.LatestGpuTemp.Should().Be(56);

            // GPU goes to sleep (Optimus d3cold): hardware reports 0°C
            mockWmi.MockGpuTemp = 0;
            worker.ProcessSingleTick();

            // Sensor must retain 56°C and NOT drop to 0 or null
            worker.LatestGpuTemp.Should().Be(56);
        }
    }
}
