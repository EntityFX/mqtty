using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Data-oriented network transport slot. It replaces the wrapper class and the
/// per-hop <see cref="Queue{T}"/> copy: a hop advances <see cref="NextHop"/> on
/// the same immutable path array. Wait ticks still advance once per Refresh.
/// </summary>
internal struct NetworkPacketSlot
{
    public INetworkPacket Packet;
    public INetwork[] Path;
    public int NextHop;
    public NetworkPacketType Type;
    public ISender? DestinationNode;
    public long CreatedTick;
    public long TransferWaitTicks;
    public bool PassTillNextTick;
    public bool Released;

    public NetworkPacketSlot(
        long tick,
        int transferWaitTicks,
        bool passTillNextTick,
        INetworkPacket packet,
        INetwork[] path,
        int nextHop,
        NetworkPacketType type,
        ISender? destinationNode)
    {
        CreatedTick = tick;
        TransferWaitTicks = transferWaitTicks;
        PassTillNextTick = passTillNextTick;
        Packet = packet;
        Path = path;
        NextHop = nextHop;
        Type = type;
        DestinationNode = destinationNode;
        Released = false;
    }

    public void ReduceTransferTicks()
    {
        TransferWaitTicks--;
    }
}
