using EntityFX.MqttY.Contracts.Network;
using System.Net.Sockets;

namespace EntityFX.MqttY.Network;

internal class NetworkPacket
{
    public NetworkPacket(Packet packet, Queue<INetwork> path, NetworkPacketType type, ISender? destionationNode)
    {
        Packet = packet;
        Path = path;
        Type = type;
        DestionationNode = destionationNode;
    }

    public Packet Packet { get; }

    internal long WaitTime { get => _waitTime; init => _waitTime = value; }

    private long _waitTime = 2;

    public Queue<INetwork> Path { get; }
    public NetworkPacketType Type { get; }
    public ISender? DestionationNode { get; }

    internal void ReduceWaitTime()
    {
        Interlocked.Decrement(ref _waitTime);
    }
}
