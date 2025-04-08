using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Counter
{
    internal class NetworkCounters: CounterGroup
    {
        private readonly List<ICounter> _counters = new List<ICounter>();

        private readonly GenericCounter _transferPackectsCounter;
        private readonly GenericCounter _inboundPackectsCounter;
        private readonly GenericCounter _outboundPackectsCounter;
        private readonly GenericCounter _inboundCounter;
        private readonly GenericCounter _outboundCounter;

        public NetworkCounters(string name)
            : base(name)
        {
            _transferPackectsCounter = new GenericCounter("TransferPackects");
            _inboundPackectsCounter = new GenericCounter("InboundPackects");
            _outboundPackectsCounter = new GenericCounter("OutboundPackects");
            _inboundCounter = new GenericCounter("Inbound");
            _outboundCounter = new GenericCounter("Outbound");
            _counters.Add(_transferPackectsCounter);
            _counters.Add(_inboundPackectsCounter);
            _counters.Add(_outboundPackectsCounter);
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
            _inboundPackectsCounter!.Increment();
            _inboundCounter!.Add(networkPacket.PacketBytes);
        }

        public void CountOutbound(NetworkPacket networkPacket)
        {
            _outboundPackectsCounter!.Increment();
            _outboundCounter!.Add(networkPacket.PacketBytes);
        }

        public void CountTransfers()
        {
            _transferPackectsCounter!.Increment();
        }
    }
}
