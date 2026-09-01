using System;
using System.Threading;
using FanControl.GigabyteWMI.Hardware;

namespace FanControl.GigabyteWMI.Background
{
    /// <summary>
    /// Dedicated asynchronous background thread for hardware dispatch and telemetry polling.
    /// Manages 3-tier hybrid control: Silent Eco (< 10%), Smart Thermal (10% - 89%), and Max Turbo (>= 90%).
    /// </summary>
    public class WmiBackgroundWorker
    {
        private readonly IGigabyteWmiService _wmiService;
        private readonly AutoResetEvent _applySignal = new AutoResetEvent(false);
        private readonly int _pollIntervalMs;
        private Thread? _thread;
        private volatile bool _running = true;

        // Telemetry state polled by background loop
        public volatile uint LatestCpuRpm = 0;
        public volatile uint LatestGpuRpm = 0;
        public volatile uint LatestCpuTemp = 45;
        public volatile uint LatestGpuTemp = 40;

        // Target fan duty cycles (0 - 100%, 255 = unset)
        private volatile byte _targetCpuDuty = 255;
        private volatile byte _targetGpuDuty = 255;
        private volatile bool _targetResetAuto = false;

        // Mode tracking: 0 = Auto, 1 = Silent Eco, 2 = Manual Dynamic, 3 = Turbo (7500 RPM)
        public int CurrentMode { get; private set; } = 0;
        private byte _appliedCpuDuty = 255;
        private byte _appliedGpuDuty = 255;
        private int _logThrottleCounter = 0;

        public WmiBackgroundWorker(IGigabyteWmiService wmiService, int pollIntervalMs = 350)
        {
            _wmiService = wmiService;
            _pollIntervalMs = pollIntervalMs;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "GigabyteWMI_BackgroundWorker"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _applySignal.Set();
            try
            {
                _thread?.Join(300);
            }
            catch { }
        }

        public void SetTargetDuty(float value, bool isGpu)
        {
            byte duty = (byte)Math.Max(0, Math.Min(100, Math.Round(value)));
            if (isGpu)
                _targetGpuDuty = duty;
            else
                _targetCpuDuty = duty;

            _targetResetAuto = false;
            _applySignal.Set();
        }

        public void RequestResetToAuto()
        {
            _targetResetAuto = true;
            _targetCpuDuty = 255;
            _targetGpuDuty = 255;
            _applySignal.Set();
        }

        /// <summary>
        /// Executes a single synchronous iteration of the worker logic.
        /// </summary>
        public void ProcessSingleTick()
        {
            if (!_wmiService.EnsureConnected())
                return;

            // 1. Process Fan Control Targets
            if (_targetResetAuto)
            {
                if (CurrentMode != 0)
                {
                    _wmiService.RestoreAutoMode();
                    CurrentMode = 0;
                    _appliedCpuDuty = 255;
                    _appliedGpuDuty = 255;
                }
                _targetResetAuto = false;
            }
            else
            {
                byte targetCpu = _targetCpuDuty;
                byte targetGpu = _targetGpuDuty;

                if (targetCpu != 255 || targetGpu != 255)
                {
                    byte cpuDuty = targetCpu != 255 ? targetCpu : (targetGpu != 255 ? targetGpu : (byte)0);
                    byte gpuDuty = targetGpu != 255 ? targetGpu : (targetCpu != 255 ? targetCpu : (byte)0);
                    byte maxActiveDuty = Math.Max(cpuDuty, gpuDuty);

                    // Tier 3: 100% Full MAX Turbo (>= 90%)
                    if (maxActiveDuty >= 90)
                    {
                        if (CurrentMode != 3)
                        {
                            _wmiService.ApplyTurboMode();
                            CurrentMode = 3;
                            _appliedCpuDuty = 100;
                            _appliedGpuDuty = 100;
                        }
                    }
                    // Tier 1: Silent Eco (< 10%)
                    else if (maxActiveDuty < 10)
                    {
                        if (CurrentMode != 1)
                        {
                            _wmiService.ApplySilentEcoMode();
                            CurrentMode = 1;
                            _appliedCpuDuty = 0;
                            _appliedGpuDuty = 0;
                        }
                    }
                    // Tier 2: Adaptive Thermal Auto (10% - 89%)
                    else
                    {
                        if (CurrentMode == 3)
                        {
                            _wmiService.ExitTurboMode();
                        }
                        
                        if (CurrentMode != 2)
                        {
                            _wmiService.RestoreAutoMode();
                            CurrentMode = 2;
                        }

                        if (cpuDuty != _appliedCpuDuty || gpuDuty != _appliedGpuDuty)
                        {
                            byte cpuRegSpeed = DutyTable.ToRegisterSpeed(cpuDuty);
                            _wmiService.SetFanSpeeds(cpuRegSpeed, gpuDuty);
                            _appliedCpuDuty = cpuDuty;
                            _appliedGpuDuty = gpuDuty;
                        }
                    }
                }
            }

            // 2. Poll Hardware Telemetry
            uint r1 = _wmiService.GetCpuRpm();
            uint r2 = _wmiService.GetGpuRpm();
            uint ct = _wmiService.GetCpuTemp();
            uint gt = _wmiService.GetGpuTemp();

            LatestCpuRpm = r1;
            LatestGpuRpm = r2;
            if (ct > 0) LatestCpuTemp = ct;
            if (gt > 0) LatestGpuTemp = gt;
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                _applySignal.WaitOne(_pollIntervalMs);
                if (!_running) break;

                try
                {
                    ProcessSingleTick();

                    if (++_logThrottleCounter >= 3)
                    {
                        _logThrottleCounter = 0;
                        _wmiService.Log($"Telemetry -> CPU: {LatestCpuRpm} RPM ({LatestCpuTemp}C) | GPU: {LatestGpuRpm} RPM ({LatestGpuTemp}C) | Mode: {CurrentMode}, Target: CPU={_targetCpuDuty}%, GPU={_targetGpuDuty}%");
                    }
                }
                catch (Exception ex)
                {
                    _wmiService.Log("WorkerLoop exception: " + ex.Message);
                }
            }
        }
    }
}
