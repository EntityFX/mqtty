using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record LoadBalancingTopology(NetworkSimulator Graph, MqttClient[] Publishers,
    MqttClient[] Receivers, IMqttBroker[] Brokers);
