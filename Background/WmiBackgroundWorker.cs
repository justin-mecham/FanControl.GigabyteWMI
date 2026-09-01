using System;
using System.Threading;
using FanControl.GigabyteWMI.Core;
using FanControl.GigabyteWMI.Hardware;
using FanControl.GigabyteWMI.StateMachine;

namespace FanControl.GigabyteWMI.Background
{
    /// <summary>
    /// Dedicated asynchronous background worker thread for non-blocking telemetry and hardware dispatch.
    /// Guarantees 0ms FanControl UI latency by caching telemetry in an immutable snapshot.
    /// </summary>
    public class WmiBackgroundWorker
    {
        private readonly IGigabyteWmiDriver _driver;
        private readonly IFanStateMachine _stateMachine;
        private readonly AutoResetEvent _applySignal = new AutoResetEvent(false);
        private readonly int _pollIntervalMs;
        private Thread? _thread;
        private volatile bool _running = true;

        // Thread-safe telemetry snapshot
        private volatile TelemetrySnapshot _latestTelemetry = TelemetrySnapshot.Default;

        // Pending target from UI sliders
        private readonly object _targetLock = new object();
        private byte _pendingCpuDuty = FanTarget.Unset;
        private byte _pendingGpuDuty = FanTarget.Unset;
        private bool _pendingReset = false;

        // Heartbeat timer tracking (2.5s cadence)
        private DateTime _lastHeartbeatTime = DateTime.MinValue;
        private int _logThrottleCounter = 0;

        public FanMode CurrentMode => _stateMachine.CurrentMode;
        public uint LatestCpuRpm => _latestTelemetry.CpuRpm;
        public uint LatestGpuRpm => _latestTelemetry.GpuRpm;
        public uint LatestCpuTemp => _latestTelemetry.CpuTemp;
        public uint LatestGpuTemp => _latestTelemetry.GpuTemp;

        public WmiBackgroundWorker(IGigabyteWmiDriver driver, IFanStateMachine? stateMachine = null, int pollIntervalMs = 350)
        {
            _driver = driver;
            _stateMachine = stateMachine ?? new FanStateMachine(driver);
            _pollIntervalMs = pollIntervalMs;
        }

        public void Start()
        {
            _running = true;
            _thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "GigabyteWMI_Worker"
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _applySignal.Set();
            try
            {
                // Wait long enough for a slow in-flight WMI tick to finish, so the
                // caller's ResetToAuto cannot interleave with pending manual writes.
                _thread?.Join(2000);
            }
            catch { }
        }

        public void SetTargetDuty(float value, bool isGpu)
        {
            byte duty = (byte)Math.Max(0, Math.Min(100, Math.Round(value)));
            lock (_targetLock)
            {
                if (isGpu)
                    _pendingGpuDuty = duty;
                else
                    _pendingCpuDuty = duty;
                _pendingReset = false;
            }
            _applySignal.Set();
        }

        public void RequestResetToAuto(bool isGpu = false)
        {
            lock (_targetLock)
            {
                if (isGpu)
                    _pendingGpuDuty = FanTarget.Unset;
                else
                    _pendingCpuDuty = FanTarget.Unset;

                if (_pendingCpuDuty == FanTarget.Unset && _pendingGpuDuty == FanTarget.Unset)
                {
                    _pendingReset = true;
                }
            }
            _applySignal.Set();
        }

        public void ProcessSingleTick()
        {
            if (!_driver.EnsureConnected())
                return;

            // 1. Extract target snapshot
            FanTarget target;
            lock (_targetLock)
            {
                target = new FanTarget(_pendingCpuDuty, _pendingGpuDuty, _pendingReset);
            }

            // 2. Dispatch Target to State Machine
            _stateMachine.ProcessTarget(target);

            // 3. Heartbeat check (every 2.5 seconds)
            DateTime now = DateTime.UtcNow;
            if ((now - _lastHeartbeatTime).TotalMilliseconds >= 2500)
            {
                _stateMachine.Heartbeat();
                _lastHeartbeatTime = now;
            }

            // 4. Poll Hardware Telemetry
            uint r1 = _driver.GetMethod("getRpm1");
            uint r2 = _driver.GetMethod("getRpm2");
            uint ct = _driver.GetMethod("getCpuTemp");
            uint gt = _driver.GetMethod("getGpuTemp1");

            uint lastCt = _latestTelemetry.CpuTemp;
            uint lastGt = _latestTelemetry.GpuTemp;
            uint lastR1 = _latestTelemetry.CpuRpm;
            uint lastR2 = _latestTelemetry.GpuRpm;

            // In MaxTurbo, Overdrive cycles ADJF every few seconds and the tach briefly
            // reads 0 around transitions; hold the last good reading there. In other modes
            // a zero is trusted (EC auto may legitimately park the fans at cool idle).
            bool maskZeroRpm = _stateMachine.CurrentMode == FanMode.MaxTurbo;
            _latestTelemetry = new TelemetrySnapshot(
                (r1 == 0 && maskZeroRpm) ? lastR1 : r1,
                (r2 == 0 && maskZeroRpm) ? lastR2 : r2,
                ct > 0 ? ct : lastCt,
                gt > 0 ? gt : lastGt,
                now
            );
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

                    if (++_logThrottleCounter >= 4)
                    {
                        _logThrottleCounter = 0;
                        _driver.Log($"Telemetry -> CPU: {LatestCpuRpm} RPM ({LatestCpuTemp}C) | GPU: {LatestGpuRpm} RPM ({LatestGpuTemp}C) | Mode: {_stateMachine.CurrentMode}, Target: CPU={_pendingCpuDuty}%, GPU={_pendingGpuDuty}%");
                    }
                }
                catch (Exception ex)
                {
                    _driver.Log("WorkerLoop exception: " + ex.Message);
                }
            }
        }
    }
}
