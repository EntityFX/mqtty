using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkSimulatorCounters : CounterGroup
    {
        private readonly double ticksPerSecond;

        private readonly List<ICounter> _counters = new List<ICounter>();
        private readonly ValueCounter<long> _ticksCounter;
        private readonly ValueCounter<TimeSpan> _virtualTimeCounter;

        private readonly TicksOptions _ticksOptions;

        private IEnumerable<ICounter> _netwotkCounters = Enumerable.Empty<ICounter>();

        public NetworkSimulatorCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;
            _ticksOptions = ticksOptions;

            _ticksCounter = new ValueCounter<long>("Ticks");
            _virtualTimeCounter = new ValueCounter<TimeSpan>("VirtualTime");

            _counters.Add(_ticksCounter);
            _counters.Add(_virtualTimeCounter);
            _counters.AddRange(Counters);
            Counters = _counters.ToArray();
        }

        public override void Refresh(long totalTicks)
        {
            _ticksCounter.Set(totalTicks);
            _virtualTimeCounter.Set(_ticksOptions.TickPeriod * totalTicks);
            base.Refresh(totalTicks);
        }

        public override IEnumerable<ICounter> Counters { 
            get => _counters.Concat(_netwotkCounters).ToArray();
            init => base.Counters = value; 
        }

        public void WithNetworks(IEnumerable<INetwork> networks)
        {
            _netwotkCounters = networks.Select(n => n.Counters);
        }
    }
}
