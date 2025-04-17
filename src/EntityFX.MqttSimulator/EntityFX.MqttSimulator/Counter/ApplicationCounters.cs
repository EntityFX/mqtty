using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    internal class ApplicationCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();
        public GenericCounter InvokeCounter { get; }
        public GenericCounter ErrorCounter { get; }

        public override IEnumerable<ICounter> Counters
        {
            get => _counters.ToArray();
            init => _counters = value.ToList();
        }

        public ApplicationCounters(string name)
            : base(name)
        {
            InvokeCounter = new GenericCounter("Invoke");
            ErrorCounter = new GenericCounter("Error");
            _counters.Add(InvokeCounter);
            _counters.Add(ErrorCounter);
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }

        public void Invoke()
        {
            InvokeCounter.Increment();
        }

        public void Error()
        {
            ErrorCounter.Increment();
        }
    }
}
