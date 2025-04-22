using EntityFX.MqttY.Contracts.Counters;
using System.Diagnostics.Metrics;

namespace EntityFX.MqttY.Counter
{
    internal class NodeCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();

        public GenericCounter SendCounter { get; }
        public GenericCounter ReceiveCounter { get; }
        public GenericCounter ErrorCounter { get; }

        public override IEnumerable<ICounter> Counters { 
            get => _counters.ToArray();
            set => _counters = value.ToList(); 
        }

        public NodeCounters(string name)
            : base(name)
        {
            SendCounter = new GenericCounter("Send");
            ReceiveCounter = new GenericCounter("Receive");
            ErrorCounter = new GenericCounter("Error");
            _counters.Add(SendCounter);
            _counters.Add(ReceiveCounter);
            _counters.Add(ErrorCounter);
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }

        public void Error()
        {
            ErrorCounter.Increment();
        }
    }
}
