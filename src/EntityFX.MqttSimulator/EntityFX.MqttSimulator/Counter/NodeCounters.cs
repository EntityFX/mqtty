using EntityFX.MqttY.Contracts.Counters;
using System.Diagnostics.Metrics;

namespace EntityFX.MqttY.Counter
{
    internal class NodeCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();

        public IIncrementableCounter SendCounter { get; }
        public IIncrementableCounter ReceiveCounter { get; }

        public override ICounter[] Counters { 
            get => _counters.ToArray();
            init => _counters = value.ToList(); 
        }

        public NodeCounters(string name)
            : base(name)
        {
            SendCounter = new GenericCounter("Send");
            ReceiveCounter = new GenericCounter("Receive");
            _counters.Add(SendCounter);
            _counters.Add(ReceiveCounter);
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }
    }
}
