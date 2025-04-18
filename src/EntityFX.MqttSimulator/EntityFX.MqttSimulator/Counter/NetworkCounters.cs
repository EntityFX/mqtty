using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters : CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private readonly double ticksPerSecond;

        private readonly GenericCounter _transferPacketsCounter;
        private readonly ValueCounter<long> _queueCounter;

        private readonly GenericCounter _outboundCounter;
        private readonly GenericCounter _outboundPacketsCounter;
        private readonly ValueCounter<double> _outboundThroughput;

        private readonly GenericCounter _inboundCounter;
        private readonly GenericCounter _inboundPacketsCounter;
        private readonly ValueCounter<double> _inboundThroughput;

        private readonly TicksOptions ticksOptions;

        private long _lastTicks;

        public NetworkCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;

            _transferPacketsCounter = new GenericCounter("TransferPackets");

            _queueCounter = new ValueCounter<long>("Queue");

            _inboundPacketsCounter = new GenericCounter("InboundPackets");
            _inboundThroughput = new ValueCounter<double>("InboundThroughput", "b/s", NormalizeUnits.Bit);
            _inboundCounter = new GenericCounter("Inbound", "B", NormalizeUnits.Byte);

            _outboundPacketsCounter = new GenericCounter("OutboundPackets");
            _outboundThroughput = new ValueCounter<double>("OutboundThroughput", "b/s", NormalizeUnits.Bit);
            _outboundCounter = new GenericCounter("Outbound", "B", NormalizeUnits.Byte);

            _counters.Add(_transferPacketsCounter);
            _counters.Add(_queueCounter);
            _counters.Add(_inboundPacketsCounter);
            _counters.Add(_inboundCounter);
            _counters.Add(_inboundThroughput);
            _counters.Add(_outboundPacketsCounter);
            _counters.Add(_outboundCounter);
            _counters.Add(_outboundThroughput);
            Counters = _counters.ToArray();
            this.ticksOptions = ticksOptions;
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

        public override void Refresh(long totalTicks)
        {
            base.Refresh(totalTicks);

            var ticksDiff = totalTicks - _lastTicks;

            if (ticksDiff == 0) return;

            var tickRps = ticksPerSecond / ticksDiff;
            //считаем за дельту тиков.
            _inboundThroughput.Set(_inboundCounter.Value * tickRps);
            _outboundThroughput.Set(_outboundCounter.Value * tickRps);

            _lastTicks = totalTicks;
        }
    }
}
