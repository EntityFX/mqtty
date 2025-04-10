using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Net;
using static System.Formats.Asn1.AsnWriter;

public class Server : Node, IServer
{
    private readonly Dictionary<string, IClient> _serverClients = new();

    public INetwork Network { get; }

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
        INetwork network, INetworkGraph networkGraph) : base(index, name, address, networkGraph)
    {
        ProtocolType = protocolType;
        Specification = specification;
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

    protected virtual Task OnReceived(NetworkPacket packet)
    {
        PacketReceived?.Invoke(this, packet);

        return Task.CompletedTask;
    }

    protected override async Task SendImplementationAsync(NetworkPacket packet)
    {
        BeforeSend(packet);

        var scope = NetworkGraph.Monitoring.WithBeginScope(NetworkGraph.TotalTicks, ref packet!, 
            $"Send packet {packet.From} to {packet.To}");
        NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, packet, NetworkLoggerType.Send, 
            $"Send packet {packet.From} to {packet.To}", ProtocolType, "Net Send", scope);
        await Network.SendAsync(packet);

        NetworkGraph.Monitoring.WithEndScope(NetworkGraph.TotalTicks, ref packet);

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

    protected override async Task ReceiveImplementationAsync(NetworkPacket packet)
    {
        NetworkGraph.Monitoring.WithEndScope(NetworkGraph.TotalTicks, ref packet);

        await OnReceived(packet);
    }
}
