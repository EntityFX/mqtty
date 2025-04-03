using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Counter;

public abstract class Node : NodeBase
{
    internal readonly NodeCounters counters;

    public override CounterGroup Counters => counters;

    protected Node(int index, string name, string address, INetworkGraph networkGraph) : base(index, name, address, networkGraph)
    {
        counters = new NodeCounters(Name ?? string.Empty);
    }

    protected override void AfterSend(NetworkPacket packet)
    {
        counters.SendCounter.Increment();
    }

    protected override void AfterReceive(NetworkPacket packet)
    {
        counters.ReceiveCounter.Increment();
    }
}
