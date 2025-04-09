using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Options;
using System.Diagnostics.Metrics;

namespace EntityFX.MqttY.Counter
{
    internal class MqttReceiverCounters : CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private GenericCounter receiveCounter;

        public MqttReceiverCounters(string name)
            : base(name) 
        {
            receiveCounter = new GenericCounter("Received");
            _counters.Add(receiveCounter);
            Counters = _counters.ToArray();
        }

        public void Receive()
        {
            receiveCounter.Increment();
        }
    }
}
