using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.Tests.Integration.Helpers;

public class MqttNetworkBulder
{
    private readonly INetworkSimulator networkSimulator;
    private readonly IMqttPacketManager mqttPacketManager;
    private readonly IMqttTopicEvaluator mqttTopicEvaluator;
    private int _nextId = 0;

    public MqttNetworkBulder(INetworkSimulator networkSimulator, IMqttPacketManager mqttPacketManager, IMqttTopicEvaluator mqttTopicEvaluator)
    {
        this.networkSimulator = networkSimulator;
        this.mqttPacketManager = mqttPacketManager;
        this.mqttTopicEvaluator = mqttTopicEvaluator;
    }

    public Network BuildTree(int branchingFactor, int depth, int clientsPerNode, int serversPerNode, TicksOptions ticksOptions)
    {
        if (branchingFactor < 1 || depth < 1)
            throw new ArgumentException("Параметры должны быть положительными числами");

        return CreateNetwork(branchingFactor, depth, clientsPerNode, serversPerNode, "net", new NetworkTypeOption()
        {
            NetworkType = "eth",
            RefreshTicks = 2,
            SendTicks = 3,
            Speed = 18750000
        }, ticksOptions);
    }

    private Network CreateNetwork(int branchingFactor, int depth, int clientsPerNode, int serversPerNode, string namePrefix, 
        NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
    {
        var ix = _nextId++;
        var name = $"n{ix}.{namePrefix}";
        var node = new Network(ix, name, name, "eth", networkTypeOption, ticksOptions);
        networkSimulator.AddNetwork(node);
        if (depth > 1)
        {
            for (int i = 0; i < branchingFactor; i++)
            {
                var child = CreateNetwork(branchingFactor, depth - 1, clientsPerNode, serversPerNode, name, networkTypeOption, ticksOptions);
                child.Link(node);

            }
        }

        CreateClients(node, namePrefix, clientsPerNode, ticksOptions);
        CreateServers(node, namePrefix, serversPerNode, ticksOptions);

        return node;
    }

    private void CreateServers(Network node, string namePrefix, int serversPerNode, TicksOptions ticksOptions)
    {
        for (int i = 0; i < serversPerNode; i++)
        {
            var ix = _nextId++;
            var name = $"mqb{ix}";
            var fullName = $"{name}.{namePrefix}";

            var address = $"mqtt://{name}";
            var broker = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, ix, fullName, address, "mqtt", "mqtt", ticksOptions);
            node.AddServer(broker);
            networkSimulator.AddServer(broker);
        }
    }

    private void CreateClients(Network node, string namePrefix, int clientsPerNode, TicksOptions ticksOptions)
    {
        for(int i = 0;i < clientsPerNode;i++)
        {
            var ix = _nextId++;
            var name = $"mqc{ix}";
            var fullName = $"{name}.{namePrefix}";

            var address = $"mqtt://{name}";
            var client = new MqttClient(mqttPacketManager, ix, fullName, address, "mqtt", "mqtt", name, ticksOptions);
            node.AddClient(client);
            networkSimulator.AddClient(client);
        }
    }
}
