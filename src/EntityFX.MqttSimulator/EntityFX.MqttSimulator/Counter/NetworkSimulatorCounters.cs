using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using System.Diagnostics;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkSimulatorCounters : CounterGroup
    {
        private readonly double _ticksPerSecond;

        private readonly List<ICounter> _counters = new List<ICounter>();
        private readonly ValueCounter<long> _ticksCounter;
        private readonly ValueCounter<long> _stepsCounter;
        private readonly ValueCounter<TimeSpan> _virtualTimeCounter;
        private readonly ValueCounter<TimeSpan> _realTimeCounter;
        private readonly ValueCounter<TimeSpan> _refreshTimeCounter;

        private readonly int _historyDepth;
        private readonly TicksOptions _ticksOptions;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly Stopwatch _refreshStopwatch = new Stopwatch();

        private IEnumerable<ICounter> _netwotkCounters = Enumerable.Empty<ICounter>();

        public NetworkSimulatorCounters(string name, string shortName, TicksOptions ticksOptions)
            : base(name, shortName, "Simulator", "SM")
        {
            _ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;
            _ticksOptions = ticksOptions;

            _stepsCounter = new ValueCounter<long>("Steps", "S$", ticksOptions.CounterHistoryDepth);
            _ticksCounter = new ValueCounter<long>("Ticks", "T$",ticksOptions.CounterHistoryDepth);
            _virtualTimeCounter = new ValueCounter<TimeSpan>("VirtualTime", "TV", ticksOptions.CounterHistoryDepth);
            _realTimeCounter = new ValueCounter<TimeSpan>("RealTime", "TR", ticksOptions.CounterHistoryDepth);
            _refreshTimeCounter = new ValueCounter<TimeSpan>("RefreshTime", "R$", ticksOptions.CounterHistoryDepth);

            _counters.AddRange(Counters);
            _counters.Add(_stepsCounter);
            _counters.Add(_ticksCounter);
            _counters.Add(_virtualTimeCounter);
            _counters.Add(_realTimeCounter);
            _counters.Add(_refreshTimeCounter);

            Counters = _counters.ToArray();
            _stopwatch.Start();
        }

        public override void Refresh(long totalTicks, long steps)
        {
            _ticksCounter.Set(totalTicks);
            _stepsCounter.Set(steps);
            _virtualTimeCounter.Set(_ticksOptions.TickPeriod * totalTicks);
            _realTimeCounter.Set(_stopwatch.Elapsed);
            base.Refresh(totalTicks, steps);
        }

        public override IEnumerable<ICounter> Counters { 
            get => _counters.Concat(_netwotkCounters).ToArray();
            set => base.Counters = value; 
        }

        public void WithNetworks(IEnumerable<INetwork> networks)
        {
            _netwotkCounters = networks.Select(n => n.Counters);
        }

        public void AddCounterValue<TValue>(string name, string shortName, TValue value)
            where TValue : struct, IEquatable<TValue>
        {
            var valueCounter = new ValueCounter<TValue>(name, shortName, _historyDepth);
            valueCounter.Set(value);

            _counters.Add(valueCounter);
        }

        internal void StartRefresh()
        {
            _refreshStopwatch.Restart();
        }

        internal void StopRefresh()
        {
            _refreshTimeCounter.Set(_refreshStopwatch.Elapsed);
            _refreshStopwatch.Reset();
        }
    }
}
