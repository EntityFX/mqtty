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

    public event EventHandler<INetworkPacket>? PacketReceived;
    public event EventHandler<IClient>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public Server(int index, string name, string address, string protocolType,
        string specification,
        TicksOptions ticksOptions, bool enableCounters) 
        : base(index, name, address, ticksOptions, enableCounters)
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

    public bool DetachClient(string name)
    {
        if (!_serverClients.TryGetValue(name, out var client)) return false;

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

        if (_serverClients.TryGetValue(client.Name, out var existingClient))
        {
            return ReferenceEquals(existingClient, client);
        }

        _serverClients[client.Name] = client;

        return true;
    }


    private bool DetachClientFromServer(IClient client)
    {
        return _serverClients.Remove(client.Name);
    }

    protected virtual void OnReceived(INetworkPacket packet)
    {
        PacketReceived?.Invoke(this, packet);
    }

    //protected override bool SendImplementation(NetworkPacket packet)
    //{
    //    var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packet!, 
    //        $"Send packet {packet.From} to {packet.To}");
    //    NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send, 
    //        $"Send packet {packet.From} to {packet.To}", ProtocolType, "Net Send", scope);
    //    var result = Network!.Send(packet);
    
    //    NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
    
    //    return result;
    //}

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

        Reset();
    }

    protected override bool CompleteReceiveImplementation(INetworkPacket packet)
    {
        var result = base.CompleteReceiveImplementation(packet);
        NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);

        OnReceived(packet);

        return result;
    }
}
