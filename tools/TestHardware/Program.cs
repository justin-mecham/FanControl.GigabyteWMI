using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.Hardware;
using FanControl.GigabyteWMI.StateMachine;

namespace FanControl.GigabyteWMI.TestHardware
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine(" Gigabyte WMI Hardware Diagnostic & State Machine Test Suite     ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            var driver = new GigabyteWmiDriver();
            var sm = new FanStateMachine(driver);

            Console.Write("\n[1/5] Connecting to GB_WMIACPI via root\\WMI... ");
            if (!driver.EnsureConnected())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED!");
                Console.WriteLine("Ensure this command is executed with Administrator privileges.");
                Console.ResetColor();
                return 1;
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("CONNECTED!");
            Console.ResetColor();

            Console.WriteLine("\n[2/5] Baseline Telemetry:");
            PrintTelemetry(driver);

            Console.WriteLine("\n[3/5] Testing Whisper Eco Mode (0% Target)...");
            sm.ProcessTarget(new FanTarget(0, 0));
            Thread.Sleep(4000);
            PrintTelemetry(driver);

            Console.WriteLine("\n[4/5] Testing Manual Fixed Mode (50% Target)...");
            sm.ProcessTarget(new FanTarget(50, 50));
            Thread.Sleep(4000);
            PrintTelemetry(driver);

            Console.WriteLine("\n[5/5] Testing 100% MAX Turbo Mode with Heartbeat...");
            sm.ProcessTarget(new FanTarget(100, 100));
            for (int i = 1; i <= 6; i++)
            {
                Thread.Sleep(1000);
                if (i % 2 == 0) sm.Heartbeat();
                PrintTelemetry(driver, $"Turbo Active ({i}s)");
            }

            Console.WriteLine("\n[*] Restoring Hardware EC Auto Mode...");
            sm.ResetToAuto();
            Thread.Sleep(3000);
            PrintTelemetry(driver, "Restored Auto");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nHardware Diagnostic Completed Successfully.\n");
            Console.ResetColor();
            return 0;
        }

        static void PrintTelemetry(IGigabyteWmiDriver driver, string label = "Telemetry")
        {
            uint cpuTemp = driver.GetMethod("getCpuTemp");
            uint gpuTemp = driver.GetMethod("getGpuTemp1");
            uint cpuRpm = driver.GetMethod("getRpm1");
            uint gpuRpm = driver.GetMethod("getRpm2");
            Console.WriteLine($"  [{label}] -> CPU: {cpuRpm} RPM ({cpuTemp}°C) | GPU: {gpuRpm} RPM ({gpuTemp}°C)");
        }
    }
}
