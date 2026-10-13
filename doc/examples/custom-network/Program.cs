using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Utils;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CustomNetwork <broker-calibration.v3.json|--ideal> <new-output-directory>");
    return 1;
}
var ideal = args[0] == "--ideal";
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Choose a new output directory.");
MqttBrokerProfile? profile = null;
string? profileHash = null;
if (!ideal)
{
    var bytes = File.ReadAllBytes(args[0]);
    using var document = JsonDocument.Parse(bytes);
    if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 3 ||
        document.RootElement.GetProperty("artifactType").GetString() != "broker-calibration")
        throw new InvalidDataException("An external v3 calibration is required.");
    profileHash = Convert.ToHexString(SHA256.HashData(bytes));
    profile = new BrokerBenchmarkRepository(System.Text.Encoding.UTF8.GetString(bytes)).Get("Mosquitto");
    profile.For(16, MqttQos.AtLeastOnce, 1);
}
var ticks = new TicksOptions
{
    TickPeriod = TimeSpan.FromMilliseconds(0.1), ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
    OutgoingWaitTicks = 1, CounterHistoryDepth = 100
};
var graph = new NetworkSimulator(new DijkstraPathFinder(), new NullNetworkLogger(), ticks, true);
try
{
    Network Area(int index, string name)
    {
        var area = new Network(index, name, name + ".local", "ethernet",
            new NetworkOptions { CapacityBytesPerSecond = 125_000_000, ThroughputWindowTicks = 100,
                TransferTicks = 1, QueueCapacity = 10_000, NetworkType = "ethernet" }, ticks, true);
        if (!graph.AddNetwork(area)) throw new InvalidOperationException("Duplicate area.");
        return area;
    }
    var lan = Area(0, "lan");
    var wan = Area(1, "wan");
    graph.Link("lan", "wan");
    graph.Link("wan", "lan");
    var generic = new GenericBuilder();
    var evaluator = generic.GetMqttTopicEvaluator();
    var packets = generic.GetMqttPacketManager(evaluator);
    var broker = new CustomBrokerBuilder(graph, packets, evaluator, profile).AddBroker(wan, ticks);
    broker.Start();
    MqttClient Client(int index, string name)
    {
        var client = new MqttClient(packets, index, name, $"mqtt://{name}.lan.local", "mqtt", "mqtt-client", name, ticks, true);
        lan.AddClient(client); graph.AddClient(client);
        return client;
    }
    var publisher = Client(1, "publisher");
    var subscriber = Client(2, "subscriber");
    graph.UpdateRoutes();
    void Until(Func<bool> condition, string phase)
    {
        for (var i = 0; !condition() && i < 20_000; i++)
            if (!graph.Refresh(false, 0)) throw new InvalidOperationException($"Simulation failed during {phase}.");
        if (!condition()) throw new TimeoutException(phase);
    }
    publisher.BeginConnect("broker1", cleanSession: true);
    subscriber.BeginConnect("broker1", cleanSession: true);
    Until(() => publisher.IsConnected && subscriber.IsConnected, "connect");
    subscriber.BeginSubscribe("custom/+", MqttQos.AtLeastOnce);
    Until(() => subscriber.IsSubscribed("custom/+"), "subscribe");
    var delivered = 0;
    subscriber.MessageReceived += (_, _) => delivered++;
    graph.ResetMeasurement();
    publisher.Publish("custom/value", new byte[16], MqttQos.AtLeastOnce);
    // A calibrated broker may fail/drop this single publication; quiescence still completes.
    Until(() => graph.IsQuiescent, "drain");
    Directory.CreateDirectory(output);
    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
    {
        mode = ideal ? "ideal-mechanism-smoke" : "custom-topology-demonstration",
        seed = 42, profileClientCount = 1, connectedClients = 2, profileSha256 = profileHash,
        delivered, ticks = graph.TotalTicks, errors = graph.Errors, metrics = broker.GetMetrics()
    }, options));
    File.WriteAllText(Path.Combine(output, "topology.graphml"), new SimpleGraphMlGenerator().SerializeNetworkGraph(graph));
    File.WriteAllText(Path.Combine(output, "topology.puml"), new PlantUmlGraphGenerator().Generate(graph));
    Console.WriteLine($"Output: {output}; delivered: {delivered}; errors: {graph.Errors}");
    return graph.Errors == 0 && (!ideal || delivered == 1) ? 0 : 2;
}
finally { graph.Clear(); }

// Extend the public builder instead of referencing internal MqttBroker constructors.
sealed class CustomBrokerBuilder : MqttNetworkBuilder
{
    public CustomBrokerBuilder(NetworkSimulator graph, IMqttPacketManager packets,
        IMqttTopicEvaluator evaluator, MqttBrokerProfile? profile)
        : base(graph, packets, evaluator, new GenericBuilder().GetClientBuilder(
            (index, name, protocol, specification, network, ticks, counters, group, amount, additional) =>
                throw new NotSupportedException("This example creates clients explicitly.")),
            _ => profile, randomSeed: 42, profileClientCount: 1) { }

    public IMqttBroker AddBroker(Network network, TicksOptions ticks)
    {
        var broker = (IMqttBroker)CreateServer(ticks, 0, "broker1", "broker1.wan.local", "mqtt://broker1.wan.local");
        network.AddServer(broker);
        networkSimulator.AddServer(broker);
        return broker;
    }
}
