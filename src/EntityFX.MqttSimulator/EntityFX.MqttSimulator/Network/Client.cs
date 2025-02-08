using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.ObjectModel;

public class Client : NodeBase, IClient
{

    public bool IsConnected { get; internal set; }

    public string ProtocolType { get; }


    public INetwork? Network { get; }

    public override NodeType NodeType => NodeType.Client;


    public event EventHandler<(string Client, byte[] Packet)>? PacketReceived;

    private string _serverName = string.Empty;

    public Client(string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph) : base(name, address, networkGraph)
    {
        Network = network;
        ProtocolType = protocolType;
    }

    public bool Connect(string server)
    {
        if (IsConnected) return true;

        if (Network == null) return false;

        var result = Network.AddClient(this);

        if (!result)
        {
            IsConnected = false;
            return false;
        }

        var remoteNode = AttachClientToServer(server);

        if (remoteNode == null)
        {
            IsConnected = false;
            return false;
        }

        _serverName = server;

        networkGraph.Monitoring.Push(this, remoteNode, null, EntityFX.MqttY.Contracts.Monitoring.MonitoringType.Connect, new { });
        IsConnected = true;

        return result;
    }

    private INode? AttachClientToServer(string server)
    {
        if (Network == null) return null;

        var node = Network.FindNode(server, NodeType.Server);
        var serverNode = node as IServer;
        if (serverNode == null) return null;

        if (serverNode.AttachClient(this))
        {
            return serverNode;
        }

        return null;
    }

    public bool Disconnect()
    {
        if (!IsConnected) return true;

        if (Network == null) return false;

        var result = DetachClientFromServer(_serverName);

        result = Network.RemoveClient(Address);

        if (!result)
        {
            IsConnected = true;
            return false;
        }

        _serverName = string.Empty;

        IsConnected = false;

        return result;
    }

    private bool DetachClientFromServer(string serverName)
    {
        if (Network == null) return false;

        var node = Network.FindNode(serverName, NodeType.Server);
        var serverNode = node as IServer;
        if (serverNode == null) return false;

        return serverNode.AttachClient(this);
    }

    public Task ReceiveAsync(string address, byte[] packet)
    {
        PacketReceived?.Invoke(this, (address, packet));
        return Task.CompletedTask;
    }

    public override async Task SendAsync(Packet packet)
    {
        if (!IsConnected) throw new InvalidOperationException("Not Connected To server");
        networkGraph.Monitoring.Push(
            packet.FromAddress, packet.FromType, packet.To, packet.ToType, 
            packet.Payload, MonitoringType.Send, new { });
        await Network!.SendAsync(packet);
    }

    public Task SendAsync(byte[] packet)
    {
        return SendAsync(new Packet(Name, _serverName, NodeType.Client, NodeType.Server, packet));
    }

    public void Send(byte[] packet)
    {
        SendAsync(packet).Wait();
    }

    public override Task ReceiveAsync(Packet packet)
    {
        networkGraph.Monitoring.Push(
            packet.FromAddress, packet.FromType, packet.To, packet.ToType,
            packet.Payload, MonitoringType.Receive, new { });
        return Task.CompletedTask;
    }
}
