using System;
using FanControl.Plugins;
using FanControl.GigabyteWMI.Hardware;
using FanControl.GigabyteWMI.Background;
using FanControl.GigabyteWMI.Sensors;

namespace FanControl.GigabyteWMI
{
    public class GigabytePlugin : IPlugin2
    {
        private readonly IPluginLogger? _logger;
        private readonly IPluginDialog? _dialog;

        private readonly IGigabyteWmiService _wmiService;
        private readonly WmiBackgroundWorker _worker;

        private GigabyteControlSensor? _cpuControl;
        private GigabyteControlSensor? _gpuControl;
        private GigabyteFanSensor? _cpuFanRpm;
        private GigabyteFanSensor? _gpuFanRpm;
        private GigabyteTempSensor? _cpuTemp;
        private GigabyteTempSensor? _gpuTemp;

        public string Name => "Gigabyte Laptop ACPI";

        public GigabytePlugin()
        {
            _wmiService = new GigabyteWmiService();
            _worker = new WmiBackgroundWorker(_wmiService);
            _worker.Start();
            _wmiService.Log("GigabytePlugin initialized (default ctor).");
        }

        public GigabytePlugin(IPluginLogger logger, IPluginDialog dialog)
        {
            _logger = logger;
            _dialog = dialog;
            _wmiService = new GigabyteWmiService();
            _worker = new WmiBackgroundWorker(_wmiService);
            _worker.Start();
            _wmiService.Log("GigabytePlugin initialized (injected ctor).");
        }

        /// <summary>
        /// Testing constructor for full hardware mocking and TDD verification.
        /// </summary>
        public GigabytePlugin(IGigabyteWmiService wmiService, WmiBackgroundWorker worker)
        {
            _wmiService = wmiService;
            _worker = worker;
        }

        public void Initialize()
        {
            _wmiService.Log("Initialize called.");
            _wmiService.EnsureConnected();
        }

        public void Load(IPluginSensorsContainer container)
        {
            _wmiService.Log("Load called - registering sensor and control cards.");

            // 1. Fan Speed Controls (CPU & GPU)
            _cpuControl = new GigabyteControlSensor("gb_cpu_fan_ctrl", "Gigabyte CPU Fan Control", _worker, isGpu: false);
            _gpuControl = new GigabyteControlSensor("gb_gpu_fan_ctrl", "Gigabyte GPU Fan Control", _worker, isGpu: true);
            container.ControlSensors.Add(_cpuControl);
            container.ControlSensors.Add(_gpuControl);

            // 2. Tachometer RPM Telemetry
            _cpuFanRpm = new GigabyteFanSensor("gb_cpu_fan_rpm", "Gigabyte CPU Fan RPM");
            _gpuFanRpm = new GigabyteFanSensor("gb_gpu_fan_rpm", "Gigabyte GPU Fan RPM");
            container.FanSensors.Add(_cpuFanRpm);
            container.FanSensors.Add(_gpuFanRpm);

            // 3. Temperature Sensors
            _cpuTemp = new GigabyteTempSensor("gb_cpu_temp", "Gigabyte CPU Temp");
            _gpuTemp = new GigabyteTempSensor("gb_gpu_temp", "Gigabyte GPU Temp");
            container.TempSensors.Add(_cpuTemp);
            container.TempSensors.Add(_gpuTemp);
        }

        public void Update()
        {
            // 100% instant non-blocking memory read (0.0001ms) - FanControl UI will never freeze
            if (_cpuFanRpm != null)
                _cpuFanRpm.Value = _worker.LatestCpuRpm;

            if (_gpuFanRpm != null)
                _gpuFanRpm.Value = _worker.LatestGpuRpm;

            if (_cpuTemp != null)
                _cpuTemp.Value = _worker.LatestCpuTemp;

            if (_gpuTemp != null)
                _gpuTemp.Value = _worker.LatestGpuTemp;
        }

        public void Close()
        {
            _wmiService.Log("Close called - stopping worker and restoring EC Auto control.");
            _worker.Stop();
            _wmiService.RestoreAutoMode();
        }
    }
}
