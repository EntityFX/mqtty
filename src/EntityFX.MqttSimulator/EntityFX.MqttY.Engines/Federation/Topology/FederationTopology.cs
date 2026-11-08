using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;

namespace EntityFX.MqttY.Federation;

public sealed class FederationTopology : IDisposable
{
    public NetworkSimulator Graph { get; }
    public IMqttBroker[] Brokers { get; }
    public ObservedMqttClient[] Publishers { get; }
    public ObservedMqttClient[] Receivers { get; }
    public List<(IMqttClient Client, int Target)> Connections { get; } = new();
    public List<(IMqttClient Client, string Filter)> Subscriptions { get; } = new();
    public List<StaticFederator> Federators { get; } = new();
    public List<MqttRelay> Relays { get; } = new();

    public FederationTopology(NetworkSimulator graph, int brokers, int publishers, int receivers)
    {
        Graph = graph; Brokers = new IMqttBroker[brokers]; Publishers = new ObservedMqttClient[publishers];
        Receivers = new ObservedMqttClient[receivers];
    }

    public void Pump() { foreach (var federator in Federators) federator.Pump(); }

    /// <summary>
    /// Federators own disjoint output clients and queues, so pumping them in
    /// parallel is safe. A single phase is fenced before the graph refresh
    /// starts; no packet can enter a network while that network is refreshing.
    /// </summary>
    public void Pump(bool parallel)
    {
        if (!parallel || Federators.Count < 2)
        {
            Pump();
            return;
        }

        Graph.RunParallel(index => Federators[index].Pump(), Federators.Count, chunkSize: 1);
    }

    private long _idleTick = -1;
    private bool _idle;

    public bool IsIdle
    {
        get
        {
            // IsIdle is polled twice per drain tick and scans all nodes; cache
            // the result for the current tick. The tick counter advances at the
            // end of Refresh, so the cache is invalidated after every advance.
            var tick = Graph.TotalTicks;
            if (_idleTick == tick)
            {
                return _idle;
            }

            _idle = Graph.IsQuiescent && Federators.All(f => f.IsIdle);
            _idleTick = tick;
            return _idle;
        }
    }

    public void Dispose() => Graph.Clear();
}
