using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;

public abstract class NodeBase : ISender
{
    protected readonly INetworkGraph NetworkGraph;

    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly Dictionary<Guid, NodePacket> monitorMessages = new Dictionary<Guid, NodePacket>();

    public Guid Id { get; private set; }

    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; }

    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }
    public int? GroupAmount { get; set; }
    public MonitoringScope? Scope { get; set; }


    //Создаём ManualResetEventSlim 
    public async Task SendAsync(Packet packet)
    {
        PreSend(packet);

        await SendImplementationAsync(packet);
    }

    private void PreSend(Packet packet)
    {
        if (monitorMessages.ContainsKey(packet.Id))
        {
            return;
        }

        monitorMessages.Add(packet.Id, new NodePacket()
        {
            Packet = packet,
            ResetEventSlim = new ManualResetEventSlim(false),
        });
    }

    //Добавляем и Снимаем ManualResetEventSlim 
    public virtual async Task ReceiveAsync(Packet packet)
    {
        await ReceiveImplementationAsync(packet);

        var monitorMessage = monitorMessages.GetValueOrDefault(packet.Id);

        if (monitorMessage == null)
        {
            return;
        }

        monitorMessage.ResetEventSlim?.Set();
    }
    

    protected abstract Task ReceiveImplementationAsync(Packet packet);

    protected abstract Task SendImplementationAsync(Packet packet);


    protected abstract void BeforeReceive(Packet packet);
    protected abstract void AfterReceive(Packet packet);

    protected abstract void BeforeSend(Packet packet);
    protected abstract void AfterSend(Packet packet);

    public NodeBase(int index, string name, string address, INetworkGraph networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
        this.NetworkGraph = networkGraph;
    }

    protected Packet GetPacket(Guid guid, string to, NodeType toType, byte[] payload, string protocol, string? category = null)
        => new Packet(Name, to, NodeType, toType, payload, protocol, category)
        {
            Id = guid
        };


    //Здесь обновляем время ождидания и триггерим ManualResetEventSlim
    public virtual void Refresh()
    {
        foreach (var packet in monitorMessages.Values.ToArray())
        {
            packet.ReduceWaitTime();
        }
    }


    //подписываемся на ManualResetEventSlim и ждём его
    //убрать цикл, ждёт N тиков (600,000?)
    protected Task<Packet?> WaitResponse(Guid packetId)
    {
        return Task.Run(() =>
        {
            while (true)
            {
                var monitorPacket = monitorMessages.GetValueOrDefault(packetId);

                if (monitorPacket == null)
                {
                    continue;
                }

                if (monitorPacket.WaitTime <= 0)
                {
                    //expired, timeout
                    monitorMessages.Remove(packetId);
                    return null;
                }

                if (monitorPacket?.ResetEventSlim?.IsSet == true)
                {
                    monitorMessages.Remove(packetId);
                    return monitorPacket.Packet;
                }
            }
        });
    }
}
