using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Counter;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal class MqttReceiverCounters : CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private GenericCounter _receiveCounter;

        public long Received => _receiveCounter.Value;

        public MqttReceiverCounters(string name, int historyDepth, bool enabled = true)
            : base(name, name.Substring(0, 2), "MqttReceiver", "MC", enabled) 
        {
            _receiveCounter = new GenericCounter("Received", "MI", historyDepth, enabled: enabled);
            _counters.Add(_receiveCounter);
            Counters = _counters.ToArray();
        }

        public void Receive()
        {
            _receiveCounter.Increment();
        }
    }
}
