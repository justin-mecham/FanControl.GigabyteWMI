using FanControl.Plugins;

namespace FanControl.GigabyteWMI.Sensors
{
    public class GigabyteFanSensor : IPluginSensor
    {
        private readonly string _id;
        private readonly string _name;

        public string Id => _id;
        public string Name => _name;
        public float? Value { get; set; }

        public GigabyteFanSensor(string id, string name)
        {
            _id = id;
            _name = name;
        }

        public void Update() { }
    }
}
