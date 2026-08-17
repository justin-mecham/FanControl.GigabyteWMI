using System;
using System.IO;
using System.Management;
using System.Threading;
using FanControl.Plugins;

namespace FanControl.GigabyteWMI
{
    public class GigabytePlugin : IPlugin2
    {
        private readonly IPluginLogger? _logger;
        private readonly IPluginDialog? _dialog;

        // Official Gigabyte 16-step hardware register lookup table extracted from ComData.dll
        private static readonly byte[] DutyTable = new byte[] {
            57, 68, 80, 91, 103, 114, 125, 137, 148, 160, 171, 183, 194, 206, 217, 229
        };

        private GigabyteControlSensor? _cpuFanControl;
        private GigabyteControlSensor? _gpuFanControl;
        private GigabyteFanSensor? _cpuFanRpm;
        private GigabyteFanSensor? _gpuFanRpm;
        private GigabyteTempSensor? _cpuTemp;
        private GigabyteTempSensor? _gpuTemp;

        // Cached WMI instances for instant 1ms execution
        private ManagementObject? _setInstance;
        private ManagementObject? _getInstance;

        // Telemetry state polled strictly by the background worker
        private volatile uint _latestRpm1 = 0;
        private volatile uint _latestRpm2 = 0;
        private volatile uint _latestCpuTemp = 0;
        private volatile uint _latestGpuTemp = 0;

        // Fan control target state
        private volatile byte _targetCpuDuty = 255;
        private volatile byte _targetGpuDuty = 255;
        private volatile bool _targetResetAuto = false;
        private readonly AutoResetEvent _applySignal = new AutoResetEvent(false);

        private Thread? _workerThread;
        private volatile bool _running = true;

        private byte _appliedCpuRegSpeed = 0;
        private byte _appliedGpuRegSpeed = 0;
        private bool _isManualEngaged = false;

        private static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "fancontrol_gigabyte_debug.log");

        public string Name
        {
            get { return "Gigabyte Laptop ACPI"; }
        }

        public GigabytePlugin()
        {
            Log("GigabytePlugin created (default ctor).");
            StartWorker();
        }

        public GigabytePlugin(IPluginLogger logger, IPluginDialog dialog)
        {
            _logger = logger;
            _dialog = dialog;
            Log("GigabytePlugin created (injected ctor).");
            StartWorker();
        }

        private void StartWorker()
        {
            _running = true;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "GigabyteWMI_Worker"
            };
            _workerThread.Start();
        }

        public void Initialize()
        {
            Log("Initialize called.");
            try
            {
                EnsureWmiInstances();
            }
            catch (Exception ex)
            {
                Log("Initialize exception: " + ex.ToString());
            }
        }

        public void Load(IPluginSensorsContainer container)
        {
            Log("Load called - registering sensor cards.");
            _cpuFanControl = new GigabyteControlSensor("gb_cpu_fan_ctrl", "Gigabyte CPU Fan Control", this, false);
            _gpuFanControl = new GigabyteControlSensor("gb_gpu_fan_ctrl", "Gigabyte GPU Fan Control", this, true);
            container.ControlSensors.Add(_cpuFanControl);
            container.ControlSensors.Add(_gpuFanControl);

            _cpuFanRpm = new GigabyteFanSensor("gb_cpu_fan_rpm", "Gigabyte CPU Fan RPM");
            _gpuFanRpm = new GigabyteFanSensor("gb_gpu_fan_rpm", "Gigabyte GPU Fan RPM");
            container.FanSensors.Add(_cpuFanRpm);
            container.FanSensors.Add(_gpuFanRpm);

            _cpuTemp = new GigabyteTempSensor("gb_cpu_temp", "Gigabyte CPU Temp");
            _gpuTemp = new GigabyteTempSensor("gb_gpu_temp", "Gigabyte GPU Temp");
            container.TempSensors.Add(_cpuTemp);
            container.TempSensors.Add(_gpuTemp);
        }

        public void Update()
        {
            // 100% instant non-blocking memory read (0.0001ms) - FanControl UI will never freeze
            uint r1 = _latestRpm1;
            uint r2 = _latestRpm2;
            uint ct = _latestCpuTemp;
            uint gt = _latestGpuTemp;

            if (_cpuFanRpm != null)
                _cpuFanRpm.Value = r1;

            if (_gpuFanRpm != null)
                _gpuFanRpm.Value = r2;

            if (_cpuTemp != null)
                _cpuTemp.Value = ct > 0 ? (float?)ct : null;

            if (_gpuTemp != null)
                _gpuTemp.Value = gt > 0 ? (float?)gt : null;
        }

        public void Close()
        {
            Log("Close called - stopping worker and resetting fans to Auto.");
            _running = false;
            _applySignal.Set();

            try
            {
                _workerThread?.Join(300);
            }
            catch { }

            try
            {
                EnsureWmiInstances();
                InvokeSetMethod("SetFixedFanStatus", 0);
                InvokeSetMethod("SetStepFanStatus", 0);
                InvokeSetMethod("SetAutoFanStatus", 1);
                InvokeSetMethod("SetFixedFanSpeed", 0);
                InvokeSetMethod("SetGPUFanDuty", 0);
                InvokeSetMethod("SetTppStatus", 0);
                InvokeSetMethod("SetWhisperMode", 0);
                Log("Restored Auto Fan Status on Close.");
            }
            catch (Exception ex)
            {
                Log("Close restore error: " + ex.Message);
            }
        }

        public void SetFanSpeed(float value, bool isGpu)
        {
            byte duty = (byte)Math.Max(0, Math.Min(100, Math.Round(value)));

            if (isGpu)
                _targetGpuDuty = duty;
            else
                _targetCpuDuty = duty;

            _targetResetAuto = false;
            _applySignal.Set();
        }

        public void ResetToAuto()
        {
            _targetResetAuto = true;
            _targetCpuDuty = 255;
            _targetGpuDuty = 255;
            _applySignal.Set();
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                // Poll telemetry and check control signals every 350ms
                _applySignal.WaitOne(350);
                if (!_running) break;

                try
                {
                    EnsureWmiInstances();

                    // 1. Process Fan Controls
                    if (_targetResetAuto)
                    {
                        if (_isManualEngaged)
                        {
                            InvokeSetMethod("SetFixedFanStatus", 0);
                            InvokeSetMethod("SetStepFanStatus", 0);
                            InvokeSetMethod("SetAutoFanStatus", 1);
                            InvokeSetMethod("SetFixedFanSpeed", 0);
                            InvokeSetMethod("SetGPUFanDuty", 0);
                            InvokeSetMethod("SetTppStatus", 0);
                            InvokeSetMethod("SetWhisperMode", 0);
                            _isManualEngaged = false;
                            _appliedCpuRegSpeed = 0;
                            _appliedGpuRegSpeed = 0;
                            Log("Restored Auto Fan Status (Hardware EC Auto Control).");
                        }
                        _targetResetAuto = false;
                    }
                    else
                    {
                        byte targetCpu = _targetCpuDuty;
                        byte targetGpu = _targetGpuDuty;

                        if (targetCpu != 255 || targetGpu != 255)
                        {
                            if (!_isManualEngaged)
                            {
                                Log("Engaging Manual Hardware Fixed Fan Mode");
                                InvokeSetMethod("SetWhisperMode", 0);
                                InvokeSetMethod("SetTppStatus", 0);
                                InvokeSetMethod("SetStepFanStatus", 0);
                                InvokeSetMethod("SetAutoFanStatus", 0);
                                InvokeSetMethod("SetFixedFanStatus", 1);
                                _isManualEngaged = true;
                            }

                            if (targetCpu != 255)
                            {
                                int cpuIndex = (int)Math.Round((targetCpu / 100.0) * (DutyTable.Length - 1));
                                cpuIndex = Math.Max(0, Math.Min(DutyTable.Length - 1, cpuIndex));
                                byte cpuRegSpeed = DutyTable[cpuIndex];

                                if (cpuRegSpeed != _appliedCpuRegSpeed)
                                {
                                    InvokeSetMethod("SetFixedFanSpeed", cpuRegSpeed);
                                    _appliedCpuRegSpeed = cpuRegSpeed;
                                    Log("Applied CPU RegSpeed: " + cpuRegSpeed + " (Step: " + cpuIndex + ", Target: " + targetCpu + "%)");
                                }
                            }

                            if (targetGpu != 255)
                            {
                                int gpuIndex = (int)Math.Round((targetGpu / 100.0) * (DutyTable.Length - 1));
                                gpuIndex = Math.Max(0, Math.Min(DutyTable.Length - 1, gpuIndex));
                                byte gpuRegSpeed = DutyTable[gpuIndex];

                                if (gpuRegSpeed != _appliedGpuRegSpeed)
                                {
                                    InvokeSetMethod("SetGPUFanDuty", gpuRegSpeed);
                                    _appliedGpuRegSpeed = gpuRegSpeed;
                                    Log("Applied GPU RegSpeed: " + gpuRegSpeed + " (Step: " + gpuIndex + ", Target: " + targetGpu + "%)");
                                }
                            }
                        }
                    }

                    // 2. Poll Telemetry (RPM & Temperatures)
                    uint r1 = InvokeGetMethod("getRpm1");
                    uint r2 = InvokeGetMethod("getRpm2");
                    uint ct = InvokeGetMethod("getCpuTemp");
                    uint gt = InvokeGetMethod("getGpuTemp1");

                    _latestRpm1 = r1;
                    _latestRpm2 = r2;
                    _latestCpuTemp = ct;
                    _latestGpuTemp = gt;
                }
                catch (Exception ex)
                {
                    Log("WorkerLoop exception: " + ex.Message);
                }
            }
        }

        private void EnsureWmiInstances()
        {
            if (_setInstance == null)
            {
                ManagementClass setClass = new ManagementClass(@"root\WMI:GB_WMIACPI_Set");
                foreach (ManagementObject obj in setClass.GetInstances())
                {
                    _setInstance = obj;
                    Log("Connected to GB_WMIACPI_Set: " + obj["InstanceName"]);
                    break;
                }
            }

            if (_getInstance == null)
            {
                ManagementClass getClass = new ManagementClass(@"root\WMI:GB_WMIACPI_Get");
                foreach (ManagementObject obj in getClass.GetInstances())
                {
                    _getInstance = obj;
                    Log("Connected to GB_WMIACPI_Get: " + obj["InstanceName"]);
                    break;
                }
            }
        }

        private void InvokeSetMethod(string methodName, byte data)
        {
            try
            {
                if (_setInstance == null) return;
                ManagementBaseObject inParams = _setInstance.GetMethodParameters(methodName);
                inParams["Data"] = data;
                _setInstance.InvokeMethod(methodName, inParams, null);
            }
            catch (Exception ex)
            {
                Log("InvokeSetMethod " + methodName + "(" + data + ") failed: " + ex.Message);
            }
        }

        private uint InvokeGetMethod(string methodName)
        {
            try
            {
                if (_getInstance == null) return 0;
                ManagementBaseObject outParams = _getInstance.InvokeMethod(methodName, null, null);
                if (outParams != null && outParams["Data"] != null)
                {
                    return Convert.ToUInt32(outParams["Data"]);
                }
            }
            catch (Exception ex)
            {
                Log("InvokeGetMethod " + methodName + " failed: " + ex.Message);
            }
            return 0;
        }

        private void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg;
            try
            {
                File.AppendAllText(DebugLogPath, line + Environment.NewLine);
            }
            catch { }

            if (_logger != null)
            {
                _logger.Log(msg);
            }
        }
    }

    public class GigabyteControlSensor : IPluginControlSensor
    {
        private readonly GigabytePlugin _plugin;
        private readonly bool _isGpu;
        private readonly string _id;
        private readonly string _name;

        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public float? Value { get; private set; }

        public GigabyteControlSensor(string id, string name, GigabytePlugin plugin, bool isGpu)
        {
            _id = id;
            _name = name;
            _plugin = plugin;
            _isGpu = isGpu;
        }

        public void Update() { }

        public void Set(float val)
        {
            Value = val;
            _plugin.SetFanSpeed(val, _isGpu);
        }

        public void Reset()
        {
            Value = null;
            _plugin.ResetToAuto();
        }
    }

    public class GigabyteFanSensor : IPluginSensor
    {
        private readonly string _id;
        private readonly string _name;

        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public float? Value { get; set; }

        public GigabyteFanSensor(string id, string name)
        {
            _id = id;
            _name = name;
        }

        public void Update() { }
    }

    public class GigabyteTempSensor : IPluginSensor
    {
        private readonly string _id;
        private readonly string _name;

        public string Id { get { return _id; } }
        public string Name { get { return _name; } }
        public float? Value { get; set; }

        public GigabyteTempSensor(string id, string name)
        {
            _id = id;
            _name = name;
        }

        public void Update() { }
    }
}
