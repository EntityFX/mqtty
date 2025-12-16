using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Counter
{
    public class ApplicationCounters : CounterGroup
    {
        private List<ICounter> _counters = new List<ICounter>();
        public GenericCounter InvokeCounter { get; }
        public GenericCounter ErrorCounter { get; }

        public override IEnumerable<ICounter> Counters
        {
            get => _counters.ToArray();
            set => _counters = value.ToList();
        }

        public ApplicationCounters(string name, int historyDepth, bool enabled = true)
            : base(name, name.Substring(0, 2), "Application", "AP", enabled)
        {
            InvokeCounter = new GenericCounter("Invoke", "IV", historyDepth);
            ErrorCounter = new GenericCounter("Error", "EE", historyDepth);
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
