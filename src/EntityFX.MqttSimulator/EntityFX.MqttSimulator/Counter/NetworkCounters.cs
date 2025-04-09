using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters: CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();

        private readonly GenericCounter _transferPacketsCounter;

        private readonly GenericCounter _outboundCounter;
        private readonly GenericCounter _outboundPacketsCounter;

        private readonly GenericCounter _inboundCounter;
        private readonly GenericCounter _inboundPacketsCounter;
        private readonly GenericCounter _inboundThroughput;

        public NetworkCounters(string name)
            : base(name)
        {
            _transferPacketsCounter = new GenericCounter("TransferPackets");

            _inboundPacketsCounter = new GenericCounter("InboundPackets");
            _inboundThroughput = new GenericCounter("InboundThroughput");
            _inboundCounter = new GenericCounter("Inbound");

            _outboundPacketsCounter = new GenericCounter("OutboundPackets");
            _outboundCounter = new GenericCounter("Outbound");

            _counters.Add(_transferPacketsCounter);
            _counters.Add(_inboundPacketsCounter);
            _counters.Add(_outboundPacketsCounter);
            _counters.Add(_inboundCounter);
            _counters.Add(_outboundCounter);
            Counters = _counters.ToArray();
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
        }
    }
}
