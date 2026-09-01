using FanControl.Plugins;
using FanControl.GigabyteWMI.Background;

namespace FanControl.GigabyteWMI.Sensors
{
    public class GigabyteControlSensor : IPluginControlSensor
    {
        private readonly WmiBackgroundWorker _worker;
        private readonly bool _isGpu;
        private readonly string _id;
        private readonly string _name;

        public string Id => _id;
        public string Name => _name;
        public float? Value { get; private set; }

        public GigabyteControlSensor(string id, string name, WmiBackgroundWorker worker, bool isGpu)
        {
            _id = id;
            _name = name;
            _worker = worker;
            _isGpu = isGpu;
        }

        public void Update() { }

        public void Set(float val)
        {
            Value = val;
            _worker.SetTargetDuty(val, _isGpu);
        }

        public void Reset()
        {
            Value = null;
            _worker.RequestResetToAuto(_isGpu);
        }
    }
}
