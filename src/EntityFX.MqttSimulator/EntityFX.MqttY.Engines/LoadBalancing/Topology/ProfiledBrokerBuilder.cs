using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using ModelNetwork = EntityFX.MqttY.Network.Network;

namespace EntityFX.MqttY.LoadBalancing;

internal sealed class ProfiledBrokerBuilder : MqttNetworkBuilder
{
    public ProfiledBrokerBuilder(NetworkSimulator graph, IMqttPacketManager packets, IMqttTopicEvaluator evaluator,
        MqttBrokerProfile? profile, int seed, int publishers)
        : base(graph, packets, evaluator, new GenericBuilder().GetClientBuilder(
            (index, name, protocol, specification, network, ticks, counters, group, amount, additional) =>
                throw new NotSupportedException("Clients are constructed explicitly.")),
            _ => profile, randomSeed: seed, profileClientCount: publishers) { }

    public IMqttBroker Add(ModelNetwork network, TicksOptions ticks, int backend)
    {
        var name = $"broker{backend:D4}";
        var broker = (IMqttBroker)CreateServer(ticks, backend, name, $"{name}.{network.Name}", $"mqtt://{name}.{network.Name}");
        network.AddServer(broker);
        networkSimulator.AddServer(broker);
        return broker;
    }
}
