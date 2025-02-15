using EntityFX.MqttY.Contracts.Monitoring;
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

    public override async Task<Packet> ReceiveAsync(Packet packet)
    {
        NetworkGraph.Monitoring.Push(packet.From, packet.FromType,
            packet.To, packet.ToType, packet.Payload, MonitoringType.Receive, packet.Category, packet.scope ?? Guid.NewGuid(), new { });

        var response = ProcessReceive(packet);
        NetworkGraph.Monitoring.Push(response.From, response.FromType,
            response.To, response.ToType, response.Payload, MonitoringType.Send, packet.Category, packet.scope ?? Guid.NewGuid(), new { });
        PacketReceived?.Invoke(this, packet);

        var result = await Network!.ReceiveAsync(response);

        return result;
    }

    protected virtual Packet ProcessReceive(Packet packet)
    {
        var payload = new List<byte>();
        payload.AddRange(packet.Payload);
        payload.Add(0xFF);
        return NetworkGraph.GetReversePacket(packet, payload.ToArray());
    }

    public override async Task<Packet> SendAsync(Packet packet)
    {
        return await Network.SendAsync(packet);
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
}
