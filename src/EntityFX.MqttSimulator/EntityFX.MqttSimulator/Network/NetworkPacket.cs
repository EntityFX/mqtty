﻿using EntityFX.MqttY.Contracts.Network;
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

    public Queue<INetwork> Path { get; }
    public NetworkPacketType Type { get; }
    public ISender? DestionationNode { get; }
}
