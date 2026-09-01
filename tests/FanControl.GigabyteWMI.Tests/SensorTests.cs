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
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);
            var sensor = new GigabyteControlSensor("gb_cpu_ctrl", "CPU Control", worker, isGpu: false);

            sensor.Set(45f);
            sensor.Value.Should().Be(45f);

            // 45% quantizes to the StepCurve tier on this EC
            worker.ProcessSingleTick();
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(0);
        }

        [Fact]
        public void ControlSensor_Reset_ShouldClearValueAndRequestAuto()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var worker = new WmiBackgroundWorker(mockDriver);
            var sensor = new GigabyteControlSensor("gb_cpu_ctrl", "CPU Control", worker, isGpu: false);

            sensor.Set(45f);
            worker.ProcessSingleTick();

            sensor.Reset();
            sensor.Value.Should().BeNull();

            worker.ProcessSingleTick();
            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(1);
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
            var mockDriver = new MockGigabyteWmiDriver();
            mockDriver.GetMethodReturns["getGpuTemp1"] = 56;
            var worker = new WmiBackgroundWorker(mockDriver);

            // Active 3D load: GPU wakes up and reports 56°C
            worker.ProcessSingleTick();
            worker.LatestGpuTemp.Should().Be(56);

            // GPU goes to sleep (Optimus d3cold): hardware reports 0°C
            mockDriver.GetMethodReturns["getGpuTemp1"] = 0;
            worker.ProcessSingleTick();

            // Sensor must retain 56°C and NOT drop to 0 or null
            worker.LatestGpuTemp.Should().Be(56);
        }
    }
}
