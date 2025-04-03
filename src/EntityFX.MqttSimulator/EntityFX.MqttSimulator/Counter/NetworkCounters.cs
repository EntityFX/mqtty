using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters: CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();

        public INodeCounter TransferCounter { get; }
        public INodeCounter InboundCounter { get; }
        public INodeCounter OutboundCounter { get; }

        public NetworkCounters(string name)
            : base(name)
        {
            TransferCounter = new GenericCounter("Transfer");
            InboundCounter = new GenericCounter("Inbound");
            OutboundCounter = new GenericCounter("Outbound");
            _counters.Add(TransferCounter);
            _counters.Add(InboundCounter);
            _counters.Add(OutboundCounter);
            Counters = _counters.ToArray();
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }
    }
}
