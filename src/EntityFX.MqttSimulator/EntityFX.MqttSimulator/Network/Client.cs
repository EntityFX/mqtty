using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.ObjectModel;
using System.Net;

public class Client : NodeBase, IClient
{

    public bool IsConnected { get; internal set; }

    public string ProtocolType { get; }

    public string Specification { get; private set; } = string.Empty;


    public INetwork? Network { get; }

    public override NodeType NodeType => NodeType.Client;

    public INode? Parent { get; set; }

    public event EventHandler<NetworkPacket>? PacketReceived;

    protected string serverName = string.Empty;

    public Client(int index, string name, string address, string protocolType, 
        string specification,
        INetwork network, INetworkGraph networkGraph)
        : base(index, name, address, networkGraph)
    {
        Network = network;
        ProtocolType = protocolType;
        Specification = specification;
    }

    public async Task<bool> ConnectAsync(string server)
    {
        if (IsConnected) return true;

        var response = await ConnectImplementationAsync(server,
            GetPacket(Guid.NewGuid(), server, NodeType.Server, new byte[] { 0xFF }, "Net", "Connect"));

        if (response == null) return false;

        return true;
    }

    protected async Task<NetworkPacket?> ConnectImplementationAsync(string server, NetworkPacket connectPacket)
    {
        if (Network == null) return null;

        var result = Network.AddClient(this);

        if (!result)
        {
            IsConnected = false;
            return null;
        }

        var remoteNode = AttachClientToServer(server);

        if (remoteNode == null)
        {
            IsConnected = false;
            return null;
        }

        var scope = NetworkGraph.Monitoring.WithBeginScope(ref connectPacket!, $"Connect {connectPacket.From} to {connectPacket.To}");
        NetworkGraph.Monitoring.Push(connectPacket, MonitoringType.Connect, $"Client {connectPacket.From} connects to server {connectPacket.To}", ProtocolType, "NET Connect");

        await SendImplementationAsync(connectPacket, false);

        var response = await WaitResponse(connectPacket.Id);

        if (response == null)
        {
            return null;
        }

        var responsePacket = response.Packet;

        NetworkGraph.Monitoring.WithEndScope(ref responsePacket!);

        serverName = server;

        IsConnected = true;

        return responsePacket;
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

        var result = DetachClientFromServer(serverName);

        result = Network.RemoveClient(Address);

        if (!result)
        {
            IsConnected = true;
            return false;
        }

        serverName = string.Empty;

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


    protected async Task SendImplementationAsync(NetworkPacket packet, bool checkConnection)
    {
        if (checkConnection && !IsConnected)
            throw new InvalidOperationException("Not Connected To server");
        await SendAsync(packet);
    }

    protected override async Task SendImplementationAsync(NetworkPacket packet)
    {
        BeforeSend(packet);

        await Network!.SendAsync(packet);
        AfterSend(packet);
    }

    public async Task SendAsync(byte[] payload, string? category = null)
    {
        await SendImplementationAsync(
            new NetworkPacket(Name, serverName, NodeType.Client, NodeType.Server, payload, ProtocolType, category), true);
    }

    public void Send(byte[] payload, string? category = null)
    {
        SendAsync(payload, category).Wait();
    }

    protected override async Task ReceiveImplementationAsync(NetworkPacket packet)
    {
        BeforeReceive(packet);
        await OnReceivedAsync(packet);
        AfterReceive(packet);
    }

    protected virtual Task OnReceivedAsync(NetworkPacket packet)
    {
        PacketReceived?.Invoke(this, packet);

        return Task.CompletedTask;
    }

    protected override void BeforeReceive(NetworkPacket packet)
    {
        NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, $"Recieve message from {packet.From} to {packet.To}", ProtocolType, "Net Receive");
    }

    protected override void AfterReceive(NetworkPacket packet)
    {
    }

    protected override void BeforeSend(NetworkPacket packet)
    {

    }

    protected override void AfterSend(NetworkPacket packet)
    {
    }
}
