﻿using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters: CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();
        private readonly double ticksPerSecond;
        private readonly GenericCounter _transferPacketsCounter;

        private readonly GenericCounter _outboundCounter;
        private readonly GenericCounter _outboundPacketsCounter;
        private readonly ValueCounter<double> _outboundThroughput;

        private readonly GenericCounter _inboundCounter;
        private readonly GenericCounter _inboundPacketsCounter;
        private readonly ValueCounter<double> _inboundThroughput;

        private readonly TicksOptions ticksOptions;

        public NetworkCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;

            _transferPacketsCounter = new GenericCounter("TransferPackets");

            _inboundPacketsCounter = new GenericCounter("InboundPackets");
            _inboundThroughput = new ValueCounter<double>("InboundThroughput", "b/s", NormalizeUnits.Bit);
            _inboundCounter = new GenericCounter("Inbound", "B", NormalizeUnits.Byte);

            _outboundPacketsCounter = new GenericCounter("OutboundPackets");
            _outboundThroughput = new ValueCounter<double>("OutboundThroughput", "b/s", NormalizeUnits.Bit);
            _outboundCounter = new GenericCounter("Outbound", "B", NormalizeUnits.Byte);

            _counters.Add(_transferPacketsCounter);
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

        public override void Refresh(long totalTicks)
        {
            base.Refresh(totalTicks);

            if (totalTicks == 0) return;

            _inboundThroughput.Set((double)_inboundCounter.Value / totalTicks * ticksPerSecond);
            _outboundThroughput.Set((double)_outboundCounter.Value / totalTicks * ticksPerSecond);
        }
    }
}
