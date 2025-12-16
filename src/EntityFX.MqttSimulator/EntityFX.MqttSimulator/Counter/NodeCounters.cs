using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    public class NodeCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();
        private readonly ValueCounter<long> _outgoingQueue;
        private readonly ValueCounter<long> _incommingQueue;
        private readonly ValueCounter<long> _receiveQueue;

        public GenericCounter SendCounter { get; }
        public GenericCounter ReceiveCounter { get; }
        public GenericCounter ErrorCounter { get; }

        public override IEnumerable<ICounter> Counters { 
            get => _counters.ToArray();
            set => _counters = value.ToList(); 
        }

        public NodeCounters(string name, string shortName, 
            string groupType, string shortGroupType, int historyDepth, bool enabled = true)
            : base(name, shortName, groupType, shortGroupType, enabled)
        {
            SendCounter = new GenericCounter("Send", "S", historyDepth, enabled: enabled);
            ReceiveCounter = new GenericCounter("Receive", "R", historyDepth, enabled: enabled);
            ErrorCounter = new GenericCounter("Error", "E", historyDepth, enabled: enabled);

            _outgoingQueue = new ValueCounter<long>("OutgoingQueue", "OQ", historyDepth, enabled: enabled);
            _incommingQueue = new ValueCounter<long>("IncommingQueue", "IQ", historyDepth, enabled: enabled);
            _receiveQueue = new ValueCounter<long>("ReceiveQueue", "RQ", historyDepth, enabled: enabled);

            _counters.Add(SendCounter);
            _counters.Add(ReceiveCounter);
            _counters.Add(ErrorCounter);
            _counters.Add(_outgoingQueue);
            _counters.Add(_incommingQueue);
            _counters.Add(_receiveQueue);
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }

        public void Error()
        {
            ErrorCounter.Increment();
        }

        public void SetOutgoingQueueLength(long queueLength)
        {
            _outgoingQueue.Set(queueLength);
        }        
        
        public void SetIncommingQueueLength(long queueLength)
        {
            _incommingQueue.Set(queueLength);
        }

        public void SetReceiveQueueLength(long queueLength)
        {
            _receiveQueue.Set(queueLength);
        }
    }
}
