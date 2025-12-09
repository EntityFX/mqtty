using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class NetworkMonitoringPacket
{
    public NetworkMonitoringPacket(long tick, int transferWaitTicks, bool passTillNextTick, 
        INetworkPacket packet, Queue<INetwork> path, NetworkPacketType type, ISender? destionationNode)
    {
        Tick = tick;
        Packet = packet;
        Path = path;
        Type = type;
        DestionationNode = destionationNode;
        Marker = packet.Category ?? string.Empty;
        _transferWaitTicks = transferWaitTicks;
        PassTillNextTick = passTillNextTick;
    }

    public long Tick { get; }
    public INetworkPacket Packet { get; }

    internal long TransferWaitTicks { get => _transferWaitTicks; set => _transferWaitTicks = value; }

    internal bool Released { get; set; } = false;
    internal bool PassTillNextTick { get; private set; }

    private long _transferWaitTicks;

    public Queue<INetwork> Path { get; }
    public NetworkPacketType Type { get; set; }
    public string Marker { get; }
    public ISender? DestionationNode { get; }

    internal void ReduceTransferTicks()
    {
        Interlocked.Decrement(ref _transferWaitTicks);
    }

    public NetworkMonitoringPacket BuildTransferPacket(long tick, int transferWaitTicks, bool passTillNextTick)
    {
        return new NetworkMonitoringPacket(tick, transferWaitTicks, passTillNextTick, Packet, Path, Type, DestionationNode);
    }
}
