using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    internal class NodeCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();

        public IIncrementable SendCounter { get; }
        public IIncrementable ReceiveCounter { get; }

        public NodeCounters(string name)
            : base(name)
        {
            SendCounter = new GenericCounter("Send");
            ReceiveCounter = new GenericCounter("Receive");
            _counters.Add(SendCounter);
            _counters.Add(ReceiveCounter);
            Counters = _counters.ToArray();
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }
    }
}
