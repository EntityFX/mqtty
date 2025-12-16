using System.Text;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.MqttY.Plugin.Mqtt.Factories;

public class GenericBuilder
{
    public TicksOptions GetDefaultTickOptions() => new TicksOptions()
    {
        OutgoingWaitTicks = 2,
        TickPeriod = TimeSpan.FromMilliseconds(1)
    };

    public NetworkOptions GetDefaultNetworkOptions() => new NetworkOptions()
    {
        NetworkType = "eth",
        TransferTicks = 2,
        Speed = 18750000
    };


    public IPathFinder GetPathFinder() => new DijkstraWeightedIndexPathFinder();

    public IClientBuilder GetClientBuilder(ClientBuilderAction clientBuilderAction) => new ActionClientBuilder(clientBuilderAction);

    public INetworkLogger GetNetworkLogger() => new NetworkLogger(false, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption() { Category = new string[] { "Refresh", "Link" } });


    public INetworkLogger GetNullNetworkLogger() => new NullNetworkLogger();

    public INetworkSimulator GetNetworkSimulator(IPathFinder pathFinder, INetworkLogger networkLogger, TicksOptions ticksOptions, bool enableCounters) =>
        new NetworkSimulator(pathFinder, networkLogger, ticksOptions, enableCounters);

    public IMqttTopicEvaluator GetMqttTopicEvaluator() => new MqttTopicEvaluator(true);

    public IMqttPacketManager GetMqttPacketManager(IMqttTopicEvaluator mqttTopicEvaluator) => new MqttNativePacketManager(mqttTopicEvaluator);

    //internal INetworkLoggerProvider GetNetworkLoggerProvider(INetworkLogger networkLogger)
    //{
    //    var logSb = new StringBuilder();
    //    var monitoringProvider = new SimpleNetworkLoggerProvider(networkLogger, logSb);
    //    monitoringProvider.Start();

    //    return new SimpleNetworkLoggerProvider(networkLogger, logSb);

    //}
}

