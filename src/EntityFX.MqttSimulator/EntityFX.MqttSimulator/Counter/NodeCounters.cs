﻿using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    public class NodeCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();
        private readonly ValueCounter<long> _queueCounter;

        public GenericCounter SendCounter { get; }
        public GenericCounter ReceiveCounter { get; }
        public GenericCounter ErrorCounter { get; }

        public override IEnumerable<ICounter> Counters { 
            get => _counters.ToArray();
            set => _counters = value.ToList(); 
        }

        public NodeCounters(string name, int historyDepth)
            : base(name)
        {
            SendCounter = new GenericCounter("Send", historyDepth);
            ReceiveCounter = new GenericCounter("Receive", historyDepth);
            ErrorCounter = new GenericCounter("Error", historyDepth);

            _queueCounter = new ValueCounter<long>("Queue", historyDepth);

            _counters.Add(SendCounter);
            _counters.Add(ReceiveCounter);
            _counters.Add(ErrorCounter);
            _counters.Add(_queueCounter);
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }

        public void Error()
        {
            ErrorCounter.Increment();
        }

        public void SetQueueLength(long queueLength)
        {
            _queueCounter.Set(queueLength);
        }
    }
}
