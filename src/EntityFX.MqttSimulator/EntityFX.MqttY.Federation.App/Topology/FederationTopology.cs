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
    { Graph = graph; Brokers = new IMqttBroker[brokers]; Publishers = new ObservedMqttClient[publishers]; Receivers = new ObservedMqttClient[receivers]; }
    public void Pump() { foreach (var federator in Federators) federator.Pump(); }
    public bool IsIdle => Graph.IsQuiescent && Federators.All(f => f.IsIdle);
    public void Dispose() => Graph.Clear();
}
