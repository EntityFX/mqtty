using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Utils;
using ModelNetwork = EntityFX.MqttY.Network.Network;

namespace EntityFX.MqttY.LoadBalancing;

public static class LoadBalancingTopologyBuilder
{
    public const string Topic = "balancing/data";

    public static LoadBalancingTopology Build(PreparedExperiment input)
    {
        var options = input.Options;
        var ticks = options.Ticks();
        var graph = new NetworkSimulator(new DijkstraPathFinder(), new NullNetworkLogger(), ticks, true);
        try
        {
            var generic = new GenericBuilder();
            var evaluator = generic.GetMqttTopicEvaluator();
            var packets = generic.GetMqttPacketManager(evaluator);
            var index = 0;
            ModelNetwork Area(string name)
            {
                var area = new ModelNetwork(index++, name, name + ".local", "ethernet", options.Network.ToOptions(), ticks, true);
                if (!graph.AddNetwork(area)) throw new InvalidOperationException("Duplicate network.");
                return area;
            }
            var lan = Area("clients");
            var brokers = new IMqttBroker[options.BackendCount];
            for (var backend = 0; backend < brokers.Length; backend++)
            {
                var previous = lan;
                for (var hop = 1; hop <= options.NetworkLength; hop++)
                {
                    var next = Area($"backend{backend}-hop{hop}");
                    graph.Link(previous.Name, next.Name);
                    graph.Link(next.Name, previous.Name);
                    previous = next;
                }
                var builder = new ProfiledBrokerBuilder(graph, packets, evaluator, input.Profile?.Profile,
                    options.Seed, input.Assignments.Count(n => n == backend));
                brokers[backend] = builder.Add(previous, ticks, backend);
                brokers[backend].Start();
            }
            MqttClient Client(string name)
            {
                var client = new MqttClient(packets, index++, name, $"mqtt://{name}.clients.local",
                    "mqtt", "mqtt-client", name, ticks, true);
                lan.AddClient(client);
                graph.AddClient(client);
                return client;
            }
            var publishers = Enumerable.Range(0, options.TotalPublishers).Select(i => Client($"publisher{i:D4}")).ToArray();
            var receivers = Enumerable.Range(0, brokers.Length).Select(i => Client($"receiver{i:D4}")).ToArray();
            graph.UpdateRoutes();
            return new(graph, publishers, receivers, brokers);
        }
        catch { graph.Clear(); throw; }
    }
}
