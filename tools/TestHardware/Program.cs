using System;
using System.Collections.Generic;
using System.Threading;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.TestHardware
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==================================================================");
            Console.WriteLine(" Gigabyte WMI Deep Hardware Diagnostic & Dual-Fan Test Suite     ");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            var service = new GigabyteWmiService();
            var results = new List<(string Phase, bool Passed, string Details)>();

            Console.Write("\n[1/6] Connecting to GB_WMIACPI via root\\WMI... ");
            if (!service.EnsureConnected())
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

            Console.WriteLine("\n[2/6] Baseline Telemetry (Initial Resting State):");
            for (int i = 1; i <= 3; i++)
            {
                uint cpuTemp = service.GetCpuTemp();
                uint gpuTemp = service.GetGpuTemp();
                uint cpuRpm = service.GetCpuRpm();
                uint gpuRpm = service.GetGpuRpm();

                Console.WriteLine($"  Baseline {i}: CPU: {cpuTemp}°C ({cpuRpm} RPM) | GPU: {gpuTemp}°C ({gpuRpm} RPM)");
                Thread.Sleep(2000);
            }

            // Phase 1: Silent Eco Mode (30s deep coast-down)
            Console.WriteLine("\n[3/6] Phase 1: Engaging Silent Eco Mode (SetWhisperMode=1)...");
            service.ApplySilentEcoMode();
            MonitorPhase(service, durationSeconds: 30, sampleIntervalSeconds: 3, label: "Silent Eco Coasting");
            uint p1CpuRpm = service.GetCpuRpm();
            uint p1GpuRpm = service.GetGpuRpm();
            bool p1Pass = p1CpuRpm <= 3800 && p1GpuRpm <= 3800;
            results.Add(("Phase 1: Silent Eco Mode (<= 3800 RPM)", p1Pass, $"CPU: {p1CpuRpm} RPM | GPU: {p1GpuRpm} RPM"));
            PrintPhaseResult(p1Pass, $"CPU: {p1CpuRpm} RPM | GPU: {p1GpuRpm} RPM (Expected <= 3800 RPM inaudible zone)");

            // Inter-phase Pause (30s)
            InterPhasePause(service, pauseSeconds: 30);

            // Phase 2: Adaptive Thermal Auto (50% target)
            Console.WriteLine("\n[4/6] Phase 2: Engaging Adaptive Thermal Mode (50% Target)...");
            service.RestoreAutoMode();
            service.SetFanSpeeds(cpuRegSpeed: 148, gpuDuty: 50);
            MonitorPhase(service, durationSeconds: 25, sampleIntervalSeconds: 3, label: "50% Thermal Mode");
            uint p2CpuRpm = service.GetCpuRpm();
            uint p2GpuRpm = service.GetGpuRpm();
            bool p2Pass = p2CpuRpm >= 2500 && p2CpuRpm <= 6000;
            results.Add(("Phase 2: Adaptive Thermal 50% (2500 - 6000 RPM)", p2Pass, $"CPU: {p2CpuRpm} RPM | GPU: {p2GpuRpm} RPM"));
            PrintPhaseResult(p2Pass, $"CPU: {p2CpuRpm} RPM | GPU: {p2GpuRpm} RPM (Expected 2500 - 6000 RPM)");

            // Inter-phase Pause (30s)
            InterPhasePause(service, pauseSeconds: 30);

            // Phase 3: High Performance Adaptive (75% target)
            Console.WriteLine("\n[5/6] Phase 3: Engaging High Performance Adaptive (75% Target)...");
            service.RestoreAutoMode();
            service.SetFanSpeeds(cpuRegSpeed: 194, gpuDuty: 75);
            MonitorPhase(service, durationSeconds: 25, sampleIntervalSeconds: 3, label: "75% Thermal Mode");
            uint p3CpuRpm = service.GetCpuRpm();
            uint p3GpuRpm = service.GetGpuRpm();
            bool p3Pass = p3CpuRpm >= 3500;
            results.Add(("Phase 3: High Performance 75% (>= 3500 RPM)", p3Pass, $"CPU: {p3CpuRpm} RPM | GPU: {p3GpuRpm} RPM"));
            PrintPhaseResult(p3Pass, $"CPU: {p3CpuRpm} RPM | GPU: {p3GpuRpm} RPM (Expected >= 3500 RPM)");

            // Inter-phase Pause (30s)
            InterPhasePause(service, pauseSeconds: 30);

            // Phase 4: Full MAX Turbo Mode (Both 100% / 7500 RPM)
            Console.WriteLine("\n[6/6] Phase 4: Engaging 100% MAX Turbo Mode (7500 RPM TPP)...");
            service.ApplyTurboMode();
            MonitorPhase(service, durationSeconds: 20, sampleIntervalSeconds: 3, label: "100% Turbo Scream");
            uint p4CpuRpm = service.GetCpuRpm();
            uint p4GpuRpm = service.GetGpuRpm();
            bool p4Pass = p4CpuRpm >= 6500 || p4GpuRpm >= 6500;
            results.Add(("Phase 4: 100% MAX Turbo (>= 6500 RPM)", p4Pass, $"CPU: {p4CpuRpm} RPM | GPU: {p4GpuRpm} RPM"));
            PrintPhaseResult(p4Pass, $"CPU: {p4CpuRpm} RPM | GPU: {p4GpuRpm} RPM (Expected >= 6500 RPM)");

            // Final: Restore Auto
            Console.WriteLine("\n[*] Final: Restoring Hardware EC Auto Mode...");
            service.RestoreAutoMode();
            MonitorPhase(service, durationSeconds: 15, sampleIntervalSeconds: 3, label: "Auto Restoring");
            uint p5CpuRpm = service.GetCpuRpm();
            uint p5GpuRpm = service.GetGpuRpm();
            bool p5Pass = true;
            results.Add(("Phase 5: Restore Auto Control", p5Pass, $"CPU: {p5CpuRpm} RPM | GPU: {p5GpuRpm} RPM"));
            PrintPhaseResult(p5Pass, $"CPU: {p5CpuRpm} RPM | GPU: {p5GpuRpm} RPM");

            // Print Final Scorecard
            Console.WriteLine("\n==================================================================");
            Console.WriteLine("                HARDWARE TEST RESULTS SCORECARD                   ");
            Console.WriteLine("==================================================================");
            bool allPassed = true;
            foreach (var r in results)
            {
                if (r.Passed)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  [PASS] {r.Phase,-44} -> {r.Details}");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"  [FAIL] {r.Phase,-44} -> {r.Details}");
                    allPassed = false;
                }
            }
            Console.ResetColor();
            Console.WriteLine("==================================================================\n");

            return allPassed ? 0 : 1;
        }

        static void PrintPhaseResult(bool passed, string message)
        {
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  -> [PASS] {message}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  -> [FAIL] {message}");
            }
            Console.ResetColor();
        }

        static void InterPhasePause(IGigabyteWmiService service, int pauseSeconds)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"\n  ⏳ [Inter-Phase Pause: Resting for {pauseSeconds}s before next phase...]");
            Console.ResetColor();

            int elapsed = 0;
            while (elapsed < pauseSeconds)
            {
                Thread.Sleep(3000);
                elapsed += 3;

                uint r1 = service.GetCpuRpm();
                uint r2 = service.GetGpuRpm();
                uint t1 = service.GetCpuTemp();
                uint t2 = service.GetGpuTemp();

                int remaining = Math.Max(0, pauseSeconds - elapsed);
                Console.WriteLine($"     [Pause - {remaining,2}s left] -> CPU: {r1,4} RPM ({t1}°C) | GPU: {r2,4} RPM ({t2}°C)");
            }
        }

        static void MonitorPhase(IGigabyteWmiService service, int durationSeconds, int sampleIntervalSeconds, string label)
        {
            int elapsed = 0;
            while (elapsed < durationSeconds)
            {
                Thread.Sleep(sampleIntervalSeconds * 1000);
                elapsed += sampleIntervalSeconds;

                uint r1 = service.GetCpuRpm();
                uint r2 = service.GetGpuRpm();
                uint t1 = service.GetCpuTemp();
                uint t2 = service.GetGpuTemp();

                int remaining = Math.Max(0, durationSeconds - elapsed);
                Console.WriteLine($"     [{label} - {remaining,2}s left] -> CPU: {r1,4} RPM ({t1}°C) | GPU: {r2,4} RPM ({t2}°C)");
            }
        }
    }
}
