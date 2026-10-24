using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Network;
using ModelNetwork = EntityFX.MqttY.Network.Network;
namespace EntityFX.MqttY.Federation;

internal sealed class FederationBrokerBuilder : MqttNetworkBuilder
{
    public FederationBrokerBuilder(NetworkSimulator graph, IMqttPacketManager packets, IMqttTopicEvaluator evaluator,
        FederationInput input, int publishers)
        : base(graph, packets, evaluator, new GenericBuilder().GetClientBuilder((a, b, c, d, e, f, g, h, i, j) => throw new NotSupportedException()),
            _ => input.Profile, input.Options.Seed, profileClientCount: publishers)
    { }
    public IMqttBroker Add(ModelNetwork network, TicksOptions ticks, int site, string? nameOverride = null)
    {
        var name = nameOverride ?? $"broker{site:D4}";
        var broker = (IMqttBroker)CreateServer(ticks, site, name, $"{name}.{network.Name}", $"mqtt://{name}.{network.Name}");
        network.AddServer(broker); networkSimulator.AddServer(broker); broker.Start(); return broker;
    }
}
