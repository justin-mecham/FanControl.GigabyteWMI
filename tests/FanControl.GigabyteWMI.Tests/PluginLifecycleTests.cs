using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using FluentAssertions;
using FanControl.Plugins;
using FanControl.GigabyteWMI.Background;
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
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);
            var plugin = new GigabytePlugin(mockWmi, worker);
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
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);
            var plugin = new GigabytePlugin(mockWmi, worker);
            var container = new TestSensorsContainer();

            plugin.Load(container);

            // Populate worker telemetry
            worker.LatestCpuRpm = 3200;
            worker.LatestGpuRpm = 3400;
            worker.LatestCpuTemp = 55;
            worker.LatestGpuTemp = 50;

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
            var mockWmi = new MockGigabyteWmiService();
            var worker = new WmiBackgroundWorker(mockWmi);
            var plugin = new GigabytePlugin(mockWmi, worker);

            plugin.Close();

            mockWmi.IsAutoModeRestored.Should().BeTrue();
        }
    }
}
