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

    //TODO: Introduce wait time: ticks to wait for, reinitilize throu each transfer

    public Queue<INetwork> Path { get; }
    public NetworkPacketType Type { get; }
    public ISender? DestionationNode { get; }
}
