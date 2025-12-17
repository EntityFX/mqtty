using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters : CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private readonly double _ticksPerSecond;

        private readonly GenericCounter _transferPacketsCounter;
        private readonly ValueCounter<long> _queueCounter;
        private readonly GenericCounter _refusedCounter;

        private readonly GenericCounter _outboundCounter;
        private readonly GenericCounter _outboundPacketsCounter;
        private readonly ValueCounter<double> _outboundThroughput;

        private readonly GenericCounter _inboundCounter;
        private readonly GenericCounter _inboundPacketsCounter;
        private readonly ValueCounter<double> _inboundThroughput;

        private readonly TicksOptions _ticksOptions;

        private long _lastTicks;

        public double AvgInboundThroughput { get; private set; }

        public NetworkCounters(string name, string shortName, TicksOptions ticksOptions, bool enabled = true, bool historyEnabled = false)
            : base(name, shortName, "NetworkCounters", "NC")
        {
            _ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;

            _transferPacketsCounter = new GenericCounter("TransferPackets", "TP", ticksOptions.CounterHistoryDepth,
                enabled: enabled, historyEnabled: historyEnabled);

            _queueCounter = new ValueCounter<long>("Queue", "NQ", ticksOptions.CounterHistoryDepth,
                enabled: enabled, historyEnabled: historyEnabled);
            _refusedCounter = new GenericCounter("Refused", "RF", ticksOptions.CounterHistoryDepth,
                enabled: enabled, historyEnabled: historyEnabled);

            _inboundPacketsCounter = new GenericCounter("InboundPackets", "IP", ticksOptions.CounterHistoryDepth,
                enabled: enabled, historyEnabled: historyEnabled);
            _inboundThroughput = new ValueCounter<double>("InboundThroughput", "IT", ticksOptions.CounterHistoryDepth, 
                "b/s", NormalizeUnits.Bit,
                enabled: enabled, historyEnabled: historyEnabled);
            _inboundCounter = new GenericCounter("Inbound", "IB", ticksOptions.CounterHistoryDepth, 
                "B", NormalizeUnits.Byte,
                enabled: enabled, historyEnabled: historyEnabled);

            _outboundPacketsCounter = new GenericCounter("OutboundPackets", "OP", ticksOptions.CounterHistoryDepth,
                enabled: enabled, historyEnabled: historyEnabled);
            _outboundThroughput = new ValueCounter<double>("OutboundThroughput", "OT", ticksOptions.CounterHistoryDepth,
                "b/s", NormalizeUnits.Bit,
                enabled: enabled, historyEnabled: historyEnabled);
            _outboundCounter = new GenericCounter("Outbound", "OB", ticksOptions.CounterHistoryDepth, 
                "B", NormalizeUnits.Byte,
                enabled: enabled, historyEnabled: historyEnabled);

            _counters.Add(_transferPacketsCounter);
            _counters.Add(_queueCounter);
            _counters.Add(_refusedCounter);
            _counters.Add(_inboundPacketsCounter);
            _counters.Add(_inboundCounter);
            _counters.Add(_inboundThroughput);
            _counters.Add(_outboundPacketsCounter);
            _counters.Add(_outboundCounter);
            _counters.Add(_outboundThroughput);
            Counters = _counters.ToArray();
            this._ticksOptions = ticksOptions;
        }

        public void AddCounter(ICounter incrementable)
        {
            _counters.Add(incrementable);
        }

        public void CountInbound(INetworkPacket networkPacket)
        {
            _inboundPacketsCounter!.Increment();
            _inboundCounter!.Add(networkPacket.PacketBytes);
        }

        public void CountOutbound(INetworkPacket networkPacket)
        {
            _outboundPacketsCounter!.Increment();
            _outboundCounter!.Add(networkPacket.PacketBytes);
        }

        public void CountTransfers()
        {
            _transferPacketsCounter!.Increment();
        }

        public void SetQueueLength(long queueLength)
        {
            _queueCounter.Set(queueLength);
        }

        public void Refuse()
        {
            _refusedCounter.Increment();
        }

        public override void Refresh(long totalTicks, long steps)
        {
            if (!Enabled)
            {
                return;
            }
            base.Refresh(totalTicks, steps);

            var ticksDiff = totalTicks - _lastTicks;

            if (ticksDiff < 100) return;

            var inboundFirstTick = _inboundCounter.TickFirstValue;

            if (inboundFirstTick == null)
            {
                return;
            }

            var inboundTickDiff = totalTicks - inboundFirstTick.Value.Key;


            var inboundTtickRps = inboundTickDiff > 0 ? _ticksPerSecond / inboundTickDiff : 0;

            var inboundDiff = _inboundCounter.Value - inboundFirstTick.Value.Value;

            //считаем за дельту тиков.
            _inboundThroughput.Set(inboundDiff * inboundTtickRps);
            AvgInboundThroughput = _inboundThroughput.HistoryValues.Any()
                ? _inboundThroughput.HistoryValues.Average(hv => hv.Value) : 0;

            if (AvgInboundThroughput >= double.PositiveInfinity)
            {

            }

            var outboundFirstTick = _inboundCounter.TickFirstValue;
            if (outboundFirstTick == null)
            {
                return;
            }

            var outboundTickDiff = totalTicks - outboundFirstTick.Value.Key;

            var outboundTickRps = _ticksPerSecond / outboundTickDiff;

            var outboundDiff = _outboundCounter.Value - outboundFirstTick.Value.Value;

            _outboundThroughput.Set(outboundDiff * outboundTickRps);



            _lastTicks = totalTicks;
        }
    }
}
