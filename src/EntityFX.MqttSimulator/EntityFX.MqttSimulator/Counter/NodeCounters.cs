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
            string groupType, string shortGroupType, int historyDepth)
            : base(name, shortName, groupType, shortGroupType)
        {
            SendCounter = new GenericCounter("Send", "S", historyDepth);
            ReceiveCounter = new GenericCounter("Receive", "R", historyDepth);
            ErrorCounter = new GenericCounter("Error", "E", historyDepth);

            _outgoingQueue = new ValueCounter<long>("OutgoingQueue", "OQ", historyDepth);
            _incommingQueue = new ValueCounter<long>("IncommingQueue", "IQ", historyDepth);
            _receiveQueue = new ValueCounter<long>("ReceiveQueue", "RQ", historyDepth);

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
