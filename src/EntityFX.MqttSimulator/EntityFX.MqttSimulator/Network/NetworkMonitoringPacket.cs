using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class NetworkMonitoringPacket
{
    public NetworkMonitoringPacket(long tick, Contracts.Network.NetworkPacket packet, Queue<INetwork> path, NetworkPacketType type, ISender? destionationNode)
    {
        Tick = tick;
        Packet = packet;
        Path = path;
        Type = type;
        DestionationNode = destionationNode;
        Marker = packet.Category ?? string.Empty;
    }

    public long Tick { get; }
    public NetworkPacket Packet { get; }

    internal long WaitTime { get => _waitTime; set => _waitTime = value; }

    internal bool Released { get; set; } = false;

    private long _waitTime = 2;

    public Queue<INetwork> Path { get; }
    public NetworkPacketType Type { get; set; }
    public string Marker { get; }
    public ISender? DestionationNode { get; }

    internal void ReduceWaitTime()
    {
        Interlocked.Decrement(ref _waitTime);
    }

    public NetworkMonitoringPacket Transfer(long tick)
    {
        return new NetworkMonitoringPacket(tick, Packet, Path, Type, DestionationNode);
    }
}
