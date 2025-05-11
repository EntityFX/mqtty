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
        private readonly ValueCounter<double> _avgInboundThroughput;

        private readonly TicksOptions _ticksOptions;

        private long _lastTicks;

        public double InboundThroughput => _inboundThroughput.Value;

        public double AvgInboundThroughput { get; private set; }

        public NetworkCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            _ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;

            _transferPacketsCounter = new GenericCounter("TransferPackets", ticksOptions.CounterHistoryDepth);

            _queueCounter = new ValueCounter<long>("Queue", ticksOptions.CounterHistoryDepth);
            _refusedCounter = new GenericCounter("Refused", ticksOptions.CounterHistoryDepth);

            _inboundPacketsCounter = new GenericCounter("InboundPackets", ticksOptions.CounterHistoryDepth);
            _inboundThroughput = new ValueCounter<double>("InboundThroughput", ticksOptions.CounterHistoryDepth, 
                "b/s", NormalizeUnits.Bit);
            _avgInboundThroughput = new ValueCounter<double>("AvgInboundThroughput", ticksOptions.CounterHistoryDepth, 
                "b/s", NormalizeUnits.Bit);
            _inboundCounter = new GenericCounter("Inbound", ticksOptions.CounterHistoryDepth, 
                "B", NormalizeUnits.Byte);

            _outboundPacketsCounter = new GenericCounter("OutboundPackets", ticksOptions.CounterHistoryDepth);
            _outboundThroughput = new ValueCounter<double>("OutboundThroughput", ticksOptions.CounterHistoryDepth,
                "b/s", NormalizeUnits.Bit);
            _outboundCounter = new GenericCounter("Outbound", ticksOptions.CounterHistoryDepth, 
                "B", NormalizeUnits.Byte);

            _counters.Add(_transferPacketsCounter);
            _counters.Add(_queueCounter);
            _counters.Add(_refusedCounter);
            _counters.Add(_inboundPacketsCounter);
            _counters.Add(_avgInboundThroughput);
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

        public void CountInbound(NetworkPacket networkPacket)
        {
            _inboundPacketsCounter!.Increment();
            _inboundCounter!.Add(networkPacket.PacketBytes);
        }

        public void CountOutbound(NetworkPacket networkPacket)
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

        public override void Refresh(long totalTicks)
        {
            base.Refresh(totalTicks);

            var ticksDiff = totalTicks - _lastTicks;

            if (ticksDiff < 100) return;

            var tickRps = _ticksPerSecond / ticksDiff;

            var inboundDiff = _inboundCounter.Value - _inboundCounter.PreviousValue;
            var outboundDiff = _outboundCounter.Value - _outboundCounter.PreviousValue;

            //считаем за дельту тиков.
            _inboundThroughput.Set(inboundDiff * tickRps);
            _outboundThroughput.Set(outboundDiff * tickRps);

            AvgInboundThroughput = _inboundThroughput.HistoryValues.Average(hv => hv.Value);
            _avgInboundThroughput.Set(AvgInboundThroughput);

            _lastTicks = totalTicks;
        }
    }
}
