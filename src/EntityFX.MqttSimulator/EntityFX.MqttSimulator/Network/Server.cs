using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;

public class Server : Node, IServer
{
    private readonly Dictionary<string, IClient> _serverClients = new();

    public INode? Parent { get; set; }


    public bool IsStarted { get; internal set; }

    public override NodeType NodeType => NodeType.Server;

    public string ProtocolType { get; }

    public string Specification { get; }

    public event EventHandler<NetworkPacket>? PacketReceived;
    public event EventHandler<IClient>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public Server(int index, string name, string address, string protocolType,
        string specification,
        TicksOptions ticksOptions) 
        : base(index, name, address, ticksOptions)
    {
        ProtocolType = protocolType;
        Specification = specification;
    }

    public bool AttachClient(IClient client)
    {
        var result = AttachClientToServer(client);

        ClientConnected?.Invoke(result, client);

        return result;
    }

    public bool DetachClient(string address)
    {
        var node = Network!.FindNode(address, NodeType.Client);

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

    protected virtual void OnReceived(NetworkPacket packet)
    {
        PacketReceived?.Invoke(this, packet);
    }

    protected override bool SendImplementation(NetworkPacket packet)
    {
        var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packet!, 
            $"Send packet {packet.From} to {packet.To}");
        NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send, 
            $"Send packet {packet.From} to {packet.To}", ProtocolType, "Net Send", scope);
        var result = Network!.Send(packet);
    
        NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
    
        return result;
    }

    public void Start()
    {
        if (IsStarted) return;

        var result = Network!.AddServer(this);

        IsStarted = result;
    }

    public void Stop()
    {
        if (!IsStarted) return;

        var result = Network!.RemoveServer(Address);

        IsStarted = !result;
    }

    protected override void BeforeReceive(NetworkPacket packet)
    {
       // NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, packet.Category);
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

    protected override bool ReceiveImplementation(NetworkPacket packet)
    {
        NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);

        OnReceived(packet);

        return true;
    }
}
