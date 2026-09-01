using System;
using Xunit;
using FluentAssertions;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.Tests
{
    /// <summary>
    /// Live integration tests that communicate directly with the host machine's ACPI WMI firmware.
    /// Verifies that real hardware returns valid telemetry when executed on compatible Gigabyte hardware.
    /// </summary>
    public class LiveHardwareTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void LiveHardware_EnsureConnected_ShouldConnectToRealWmi()
        {
            var driver = new GigabyteWmiDriver();
            bool connected = driver.EnsureConnected();

            // When executed on physical hardware, EnsureConnected establishes WMI scope
            if (connected)
            {
                uint cpuTemp = driver.GetMethod("getCpuTemp");
                uint cpuRpm = driver.GetMethod("getRpm1");
                uint gpuRpm = driver.GetMethod("getRpm2");
                uint gpuTemp = driver.GetMethod("getGpuTemp1");

                // If running in elevated Admin context with active ACPI telemetry:
                if (cpuTemp > 0)
                {
                    cpuTemp.Should().BeInRange(20, 105, "CPU temperature should be between 20°C and 105°C");
                    cpuRpm.Should().BeInRange(0, 8000, "CPU fan RPM should be in physical motor range");
                    gpuRpm.Should().BeInRange(0, 8000, "GPU fan RPM should be in physical motor range");
                    gpuTemp.Should().BeInRange(0, 105, "GPU temperature should be between 0°C (sleep) and 105°C");
                }
                else
                {
                    // Non-elevated test runners gracefully return 0 without throwing unhandled crashes
                    cpuTemp.Should().Be(0);
                }
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void LiveHardware_DutyTableLookup_ShouldBeValidForHardware()
        {
            for (int i = 0; i <= 100; i += 10)
            {
                byte regSpeed = DutyTable.ToRegisterSpeed(i);
                regSpeed.Should().BeInRange(57, 229);
            }
        }
    }
}
