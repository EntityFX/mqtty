﻿using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using System.Net;
using static System.Formats.Asn1.AsnWriter;

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

    public Server(int index, string name, string address, string protocolType,
        INetwork network, INetworkGraph networkGraph) : base(index, name, address, networkGraph)
    {
        ProtocolType = protocolType;
        Network = network;
    }

    public bool AttachClient(IClient client)
    {
        var result = AttachClientToServer(client);

        ClientConnected?.Invoke(result, client);

        return result;
    }

    public bool DetachClient(string address)
    {
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

    public override async Task<Packet> ReceiveWithResponseAsync(Packet packet)
    {
        BeforeReceive(packet);

        NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, packet.Category, packet.Scope);
        //NetworkGraph.Monitoring.WithEndScope(ref packet);
        Tick();
        var response = OnReceivedWithResponse(packet);

        PacketReceived?.Invoke(this, packet);
        AfterReceive(packet);
        var receivePacket = await Network!.ReceiveWithResponseAsync(response);
        NetworkGraph.Monitoring.WithEndScope(ref receivePacket);

        return receivePacket;
    }

    public override async Task ReceiveAsync(Packet packet)
    {
        BeforeReceive(packet);
        NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, packet.Category, packet.Scope);
        Tick();
        NetworkGraph.Monitoring.WithEndScope(ref packet);

        await OnReceived(packet);
        AfterReceive(packet);
    }


    protected virtual Packet OnReceivedWithResponse(Packet packet)
    {
        var payload = new List<byte>();
        payload.AddRange(packet.Payload);
        payload.Add(0xFF);
        PacketReceived?.Invoke(this, packet);
        return NetworkGraph.GetReversePacket(packet, payload.ToArray(), packet.Category);
    }

    protected virtual Task OnReceived(Packet packet)
    {
        PacketReceived?.Invoke(this, packet);

        return Task.CompletedTask;
    }


    public override async Task<Packet> SendWithResponseAsync(Packet packet)
    {
        BeforeSend(packet);
        var scope = NetworkGraph.Monitoring.WithBeginScope(ref packet!, $"Send packet {packet.From} to {packet.To}");
        NetworkGraph.Monitoring.Push(packet, MonitoringType.Send, packet.Category, scope);
        Tick();
        var result = await Network.SendWithResponseAsync(packet);

        NetworkGraph.Monitoring.WithEndScope(ref packet);

        AfterSend(packet);
        return result;
    }

    public override async Task SendAsync(Packet packet)
    {
        BeforeSend(packet);

        var scope = NetworkGraph.Monitoring.WithBeginScope(ref packet!, $"Send packet {packet.From} to {packet.To}");
        NetworkGraph.Monitoring.Push(packet, MonitoringType.Send, packet.Category, scope);
        Tick();
        await Network.SendAsync(packet);

        NetworkGraph.Monitoring.Push(packet, MonitoringType.Send, packet.Category);
        NetworkGraph.Monitoring.WithEndScope(ref packet);

        AfterSend(packet);
    }

    public void Start()
    {
        if (IsStarted) return;

        var result = Network.AddServer(this);

        IsStarted = result;
    }

    public void Stop()
    {
        if (!IsStarted) return;

        var result = Network.RemoveServer(Address);

        IsStarted = !result;
    }

    protected override void BeforeReceive(Packet packet)
    {
        NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, packet.Category);
    }

    protected override void AfterReceive(Packet packet)
    {
 
    }

    protected override void BeforeSend(Packet packet)
    {
    }

    protected override void AfterSend(Packet packet)
    {
    }
}
