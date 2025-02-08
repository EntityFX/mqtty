﻿using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Net;

public class Server : NodeBase, IServer
{
    private readonly Dictionary<string, IClient> _serverClients = new();

    public INetwork Network { get; }


    public bool IsStarted { get; internal set; }

    public override NodeType NodeType => NodeType.Server;

    public string ProtocolType { get; }

    public event EventHandler<Packet>? PacketReceived;
    public event EventHandler<IClient>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public Server(string name, string address, string protocolType,
        INetwork network, INetworkGraph networkGraph) : base(name, address, networkGraph)
    {
        ProtocolType = protocolType;
        Network = network;
    }

    public bool AttachClient(IClient client)
    {
        if (Network == null) return false;

        if (client == null) return false;

        var result = AttachClientToServer(client);

        ClientConnected?.Invoke(result, client);

        return result;
    }

    public bool DetachClient(string address)
    {
        if (Network == null) return false;

        var node = Network.FindNode(address, NodeType.Client);

        var client = node as IClient;

        if (client == null) return false;

        var result = DetachClientFromServer(client);

        ClientDisconnected?.Invoke(result, client.Name);

        return result;
    }

    public IEnumerable<IClient> GetServerClients()
    {
        return _serverClients.Values;
    }

    private bool AttachClientToServer(IClient client)
    {
        if (Network == null) return false;

        if (_serverClients.ContainsKey(client.Address))
        {
            return false;
        }

        _serverClients[client.Name] = client;

        return true;
    }


    private bool DetachClientFromServer(IClient client)
    {
        if (!_serverClients.ContainsKey(client.Address))
        {
            return false;
        }

        _serverClients.Remove(client.Address);

        return true;
    }

    public override Task<Packet> ReceiveAsync(Packet packet)
    {
        networkGraph.Monitoring.Push(packet.From, packet.FromType, 
            packet.To, packet.ToType, packet.Payload, MonitoringType.Receive, new { });
        PacketReceived?.Invoke(this, packet);

        return Task.FromResult(ProcessReceive(packet));
    }

    protected virtual Packet ProcessReceive(Packet packet)
    {
        return networkGraph.GetReversePacket(packet);
    }

    public override async Task<Packet> SendAsync(Packet packet)
    {
        return await Network.SendAsync(packet);
    }

    public void Start()
    {
        if (IsStarted) return;

        if (Network == null) return;

        var result = Network.AddServer(this);

        IsStarted = result;
    }

    public void Stop()
    {
        if (!IsStarted) return;

        if (Network == null) return;

        var result = Network.RemoveServer(Address);

        IsStarted = !result;
    }
}
