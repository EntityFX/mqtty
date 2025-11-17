using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;

public class Client : Node, IClient
{

    public bool IsConnected { get; internal set; }

    public string ProtocolType { get; }

    public string Specification { get; private set; } = string.Empty;


    public INetwork? Network { get; internal set; }

    public override NodeType NodeType => NodeType.Client;

    public INode? Parent { get; set; }

    public event EventHandler<NetworkPacket>? PacketReceived;

    protected string ServerName = string.Empty;

    public Client(int index, string name, string address, string protocolType, 
        string specification,
        TicksOptions ticksOptions)
        : base(index, name, address, ticksOptions)
    {
        ProtocolType = protocolType;
        Specification = specification;
    }

    public bool Connect(string server)
    {
        if (IsConnected) return true;

        var response = ConnectImplementation(server,
            GetPacket(Guid.NewGuid(), server, NodeType.Server, new byte[] { 0xFF }, "Net", "Connect"));

        if (response == null) return false;

        return true;
    }

    public bool BeginConnect(string server)
    {
        if (IsConnected) return true;

        return BeginConnectImplementation(server,
            GetPacket(Guid.NewGuid(), server, NodeType.Server, new byte[] { 0xFF }, "Net", "Connect"));
    }

    public bool CompleteConnect(ResponsePacket response)
    {
        return CompleteConnectImplementation(response.Packet);
    }

    protected NetworkPacket? ConnectImplementation(string server, NetworkPacket connectPacket)
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

        var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref connectPacket!, 
            $"Connect {connectPacket.From} to {connectPacket.To}");
        NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, connectPacket, NetworkLoggerType.Connect, 
            $"Client {connectPacket.From} connects to server {connectPacket.To}", ProtocolType, "NET Connect");

        result = Send(connectPacket, false);

        if (!result)
        {
            IsConnected = false;
            return null;
        }

        var response = WaitResponse(connectPacket.Id);

        if (response == null)
        {
            counters.Error();
            return null;
        }

        var responsePacket = response.Packet;

        NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref responsePacket!);

        ServerName = server;

        IsConnected = true;

        return responsePacket;
    }

    protected bool BeginConnectImplementation(string server, NetworkPacket connectPacket)
    {
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

        var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref connectPacket!,
            $"Connect {connectPacket.From} to {connectPacket.To}");
        NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, connectPacket, NetworkLoggerType.Connect,
            $"Client {connectPacket.From} connects to server {connectPacket.To}", ProtocolType, "NET Connect");

        result = Send(connectPacket, false);

        if (!result)
        {
            IsConnected = false;
            return false;
        }

        return true;
    }

    protected bool CompleteConnectImplementation(NetworkPacket response)
    {
        var responsePacket = response;

        NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref responsePacket!);

        ServerName = responsePacket.From;

        IsConnected = true;

        return true;
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

        var result = DetachClientFromServer(ServerName);

        result = Network.RemoveClient(Address);

        if (!result)
        {
            IsConnected = true;
            return false;
        }

        ServerName = string.Empty;

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


    protected bool Send(NetworkPacket packet, bool checkConnection)
    {
        if (checkConnection && !IsConnected)
            throw new InvalidOperationException("Not Connected To server");
         
        return Send(packet);
    }

    protected override bool SendImplementation(NetworkPacket packet)
    {
        var result = Network!.Send(packet);

        return result;
    }

    public bool Send(byte[] payload, string? category = null)
    {
        var result = Send(
            new NetworkPacket(
                Guid.NewGuid(), null,
                Name, ServerName, NodeType.Client, NodeType.Server, 
            payload, ProtocolType, HeaderBytes: 0, WillWait: false, category), true);

        return result;
    }

    protected override bool ReceiveImplementation(NetworkPacket packet)
    {
        var result = base.ReceiveImplementation(packet);
        OnReceived(packet);

        return result;
    }

    protected virtual void OnReceived(NetworkPacket packet)
    {
        PacketReceived?.Invoke(this, packet);
    }

    protected override void BeforeReceive(NetworkPacket packet)
    {
        NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Receive, 
            $"Recieve message from {packet.From} to {packet.To}", ProtocolType, "Net Receive");
    }

    protected override void AfterReceive(NetworkPacket packet) 
    {
        base.AfterReceive(packet);
    }

    protected override void BeforeSend(NetworkPacket packet)
    {
        base.BeforeSend(packet);
    }

    protected override void AfterSend(NetworkPacket packet)
    {
        base.AfterSend(packet);
    }

    public bool BeginDisconnect()
    {
        throw new NotImplementedException();
    }

    public bool CompleteDisconnect()
    {
        throw new NotImplementedException();
    }
}
