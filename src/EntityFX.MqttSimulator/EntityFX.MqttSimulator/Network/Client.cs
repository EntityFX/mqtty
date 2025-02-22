using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.ObjectModel;
using System.Net;

public class Client : NodeBase, IClient
{

    public bool IsConnected { get; internal set; }

    public string ProtocolType { get; }


    public INetwork? Network { get; }

    public override NodeType NodeType => NodeType.Client;


    public event EventHandler<Packet>? PacketReceived;

    protected string serverName = string.Empty;

    public Client(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph)
        : base(index, name, address, networkGraph)
    {
        Network = network;
        ProtocolType = protocolType;
    }

    public async Task<bool> ConnectAsync(string server)
    {
        if (IsConnected) return true;

        var response = await ConnectImplementationAsync(server,
            GetPacket(server, NodeType.Server, new byte[] { 0xFF }, "Connect"));

        if (response == null) return false;

        return true;
    }

    protected async Task<Packet?> ConnectImplementationAsync(string server, Packet connectPacket)
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

        var payload = new byte[] { 0xFF };
        var scope = NetworkGraph.Monitoring.WithBeginScope(ref connectPacket!, $"Connect {connectPacket.From} to {connectPacket.To}");
        NetworkGraph.Monitoring.Push(connectPacket, MonitoringType.Connect, $"Client {connectPacket.From} connects to server {connectPacket.To}");
        var response = await SendWithResponseImplementationAsync(connectPacket);
        NetworkGraph.Monitoring.WithEndScope(ref response!);

        serverName = server;

        IsConnected = true;

        return response;
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

    public override async Task<Packet> SendWithResponseAsync(Packet packet)
    {
        if (!IsConnected) throw new InvalidOperationException("Not Connected To server");
        return await SendWithResponseImplementationAsync(packet);
    }

    private async Task<Packet> SendWithResponseImplementationAsync(Packet packet)
    {
        BeforeSend(packet);
        var response = await Network!.SendWithResponseAsync(packet);
        AfterSend(packet);
        return response;
    }

    public override async Task SendAsync(Packet packet)
    {
        if (!IsConnected) throw new InvalidOperationException("Not Connected To server");

        BeforeSend(packet);
        await Network!.SendAsync(packet);
        AfterSend(packet);
    }

    public async Task<byte[]> SendWithResponseAsync(byte[] payload, string? category = null)
    {
        var result = await SendWithResponseAsync(GetPacket(serverName, NodeType.Server, payload, category));

        return result.Payload;
    }

    public byte[] SendWithResponse(byte[] payload, string? category = null)
    {
        return SendWithResponseAsync(payload, category).Result;
    }

    public async Task SendAsync(byte[] payload, string? category = null)
    {
        await SendAsync(new Packet(Name, serverName, NodeType.Client, NodeType.Server, payload, category));
    }

    public void Send(byte[] payload, string? category = null)
    {
        SendAsync(payload, category).Wait();
    }

    public override Task<Packet> ReceiveWithResponseAsync(Packet packet)
    {
        BeforeReceive(packet);
        AfterReceive(packet);
        return Task.FromResult(packet);
    }

    public override async Task ReceiveAsync(Packet packet)
    {
        BeforeReceive(packet);
        await OnReceivedAsync(packet);
        AfterReceive(packet);
    }

    protected virtual Task<Packet> OnReceivedWithResponseAsync(Packet packet)
    {
        PacketReceived?.Invoke(this, packet);

        return Task.FromResult(packet);
    }

    protected virtual Task OnReceivedAsync(Packet packet)
    {
        PacketReceived?.Invoke(this, packet);

        return Task.CompletedTask;
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
