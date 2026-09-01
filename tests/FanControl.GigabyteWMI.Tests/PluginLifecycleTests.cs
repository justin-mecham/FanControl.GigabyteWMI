using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using FluentAssertions;
using FanControl.Plugins;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.Background;
using FanControl.GigabyteWMI.StateMachine;
using FanControl.GigabyteWMI.Tests.Mocks;

namespace FanControl.GigabyteWMI.Tests
{
    public class TestSensorsContainer : IPluginSensorsContainer
    {
        public List<IPluginControlSensor> ControlSensors { get; } = new List<IPluginControlSensor>();
        public List<IPluginSensor> FanSensors { get; } = new List<IPluginSensor>();
        public List<IPluginSensor> TempSensors { get; } = new List<IPluginSensor>();
    }

    public class PluginLifecycleTests
    {
        [Fact]
        public void PluginLoad_ShouldRegisterAllCards()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var stateMachine = new FanStateMachine(mockDriver);
            var worker = new WmiBackgroundWorker(mockDriver, stateMachine);
            var plugin = new GigabytePlugin(mockDriver, stateMachine, worker);
            var container = new TestSensorsContainer();

            plugin.Initialize();
            plugin.Load(container);

            container.ControlSensors.Should().HaveCount(2); // CPU & GPU Controls
            container.FanSensors.Should().HaveCount(2);     // CPU & GPU RPM
            container.TempSensors.Should().HaveCount(2);    // CPU & GPU Temp
        }

        [Fact]
        public void PluginUpdate_MustBeSubMillisecondNonBlocking()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            mockDriver.GetMethodReturns["getRpm1"] = 3200;
            mockDriver.GetMethodReturns["getRpm2"] = 3400;
            mockDriver.GetMethodReturns["getCpuTemp"] = 55;
            mockDriver.GetMethodReturns["getGpuTemp1"] = 50;

            var stateMachine = new FanStateMachine(mockDriver);
            var worker = new WmiBackgroundWorker(mockDriver, stateMachine);
            var plugin = new GigabytePlugin(mockDriver, stateMachine, worker);
            var container = new TestSensorsContainer();

            plugin.Load(container);
            worker.ProcessSingleTick();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                plugin.Update();
            }
            sw.Stop();

            // 1,000 updates should complete in well under 50ms (average < 0.05ms per update)
            sw.ElapsedMilliseconds.Should().BeLessThan(50);

            container.FanSensors[0].Value.Should().Be(3200);
            container.FanSensors[1].Value.Should().Be(3400);
            container.TempSensors[0].Value.Should().Be(55);
            container.TempSensors[1].Value.Should().Be(50);
        }

        [Fact]
        public void PluginClose_ShouldStopWorkerAndRestoreAuto()
        {
            var mockDriver = new MockGigabyteWmiDriver();
            var stateMachine = new FanStateMachine(mockDriver);
            var worker = new WmiBackgroundWorker(mockDriver, stateMachine);
            var plugin = new GigabytePlugin(mockDriver, stateMachine, worker);

            plugin.Close();

            mockDriver.LastSetMethods["SetAutoFanStatus"].Should().Be(1);
            mockDriver.LastSetMethods["SetFixedFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetStepFanStatus"].Should().Be(0);
            mockDriver.LastSetMethods["SetWhisperMode"].Should().Be(0);
        }
    }
}
