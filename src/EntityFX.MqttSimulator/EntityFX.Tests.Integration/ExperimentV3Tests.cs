using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.MqttRelay.App;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class ExperimentV3Tests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private static MqttRelayExperimentOptions Options() => new()
    {
        Brokers = 1, NetworkLength = 1, ClientsPerBroker = 2,
        PublishQos = MqttQos.AtLeastOnce, SubscribeQos = MqttQos.AtLeastOnce,
        PayloadBytes = 16, BrokerAssignmentMode = MqttBrokerAssignmentMode.Mosquitto,
        WarmupTicks = 100, MeasurementTicks = 200, DrainTimeoutTicks = 2000,
        OfferedRps = 100, RandomSeed = 42
    };
    private static TicksOptions Ticks() => new() { TickPeriod = TimeSpan.FromMilliseconds(1), OutgoingWaitTicks = 1,
        ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
    private static NetworkOptions Network() => new() { NetworkType = "eth", CapacityBytesPerSecond = 100_000_000,
        QueueCapacity = 100_000, ThroughputWindowTicks = 100, TransferTicks = 1 };
    private static void Mode(MqttRelayExperimentOptions options, string property, string value)
    {
        if (property == "TopologyMode") options.TopologyMode = Enum.Parse<ExperimentTopologyMode>(value);
        else options.ProfileClientCountMode = Enum.Parse<ProfileClientCountMode>(value);
    }
    private static string Temp() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mqtty-v3-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestMethod]
    public void Options_RejectUnknownTopologyAndProfileCountMode()
    {
        var options = Options(); options.TopologyMode = (ExperimentTopologyMode)999;
        Assert.ThrowsException<InvalidDataException>(() => options.Validate());
        options = Options(); options.ProfileClientCountMode = (ProfileClientCountMode)999;
        Assert.ThrowsException<InvalidDataException>(() => options.Validate());
    }

    [DataTestMethod]
    [DataRow("BrokerFidelity", "Publishers", 2, 3)]
    [DataRow("BrokerFidelity", "ConnectedClients", 3, 3)]
    [DataRow("MqttRelay", "Publishers", 2, 6)]
    [DataRow("MqttRelay", "ConnectedClients", 6, 6)]
    public void Topology_SelectsConfiguredProfileCount_AndExportsActualConnections(string topology, string countMode, int selected, int connected)
    {
        var root = Temp();
        try
        {
            var options = Options();
            Mode(options, "TopologyMode", topology); Mode(options, "ProfileClientCountMode", countMode);
            var profile = BrokerProfileTestData.Create("Mosquitto", 16, 1000);
            foreach (var qos in Enum.GetValues<MqttQos>())
                profile.MessageSizes[16].Qos[qos] = new CalibratedMqttQosProfile(new[] {
                    new CalibratedMqttQosSample(2, 1000, 0, 0, qos == MqttQos.AtMostOnce ? null : new(0,0,0,0,0,0)),
                    new CalibratedMqttQosSample(3, 1000, 1, 0, qos == MqttQos.AtMostOnce ? null : new(0,0,0,0,0,0)),
                    new CalibratedMqttQosSample(6, 1000, 1, 0, qos == MqttQos.AtMostOnce ? null : new(0,0,0,0,0,0)) });
            var runner = new MqttRelayExperimentRunner(Ticks(), Network(), new Repository(profile));
            var provenance = new ExperimentProvenance("y", "b", "p");
            var first = runner.Run(options, root, provenance);
            var second = runner.Run(options, root, provenance);
            Assert.AreEqual(ExperimentRunStatus.Completed, first.Summary.Status, first.Summary.Error);
            Assert.AreEqual(JsonSerializer.Serialize(first.Summary), JsonSerializer.Serialize(second.Summary));
            Assert.AreEqual(selected == 2 ? 0L : first.Summary.Simulated.Attempted, first.Summary.Simulated.PublishFailed);
            Assert.AreEqual(selected == 2 ? first.Summary.Simulated.Attempted : 0L, first.Summary.Simulated.Completed);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(first.RunDirectory, "experiment-manifest.json")));
            var counts = manifest.RootElement.GetProperty("brokerClientCounts").EnumerateArray().Single();
            Assert.AreEqual(connected, counts.GetProperty("connectedClients").GetInt32());
            Assert.AreEqual(selected, counts.GetProperty("profileClients").GetInt32());
            if (topology == "BrokerFidelity")
                Assert.AreEqual(first.Summary.Simulated.Admitted, first.Summary.Simulated.ExpectedDeliveries);
        }
        finally { Cleanup(root); }
    }

    [TestMethod]
    public void ExperimentCommand_HashesAndUsesExternalProfileBytes()
    {
        var root = Temp();
        try
        {
            Git(root, "init"); Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "test");
            var profile = new JsonObject { ["schemaVersion"] = 2, ["brokers"] = new JsonArray(new JsonObject {
                ["name"] = "Mosquitto", ["messageSizes"] = new JsonArray(new JsonObject { ["messageBytes"] = 16,
                    ["qos"] = new JsonArray(Enumerable.Range(0, 3).Select(q => (JsonNode)new JsonObject { ["qos"] = q,
                        ["samples"] = new JsonArray(new JsonObject { ["clients"] = 1, ["capacityRps"] = 1000,
                            ["publishFailureRate"] = 1, ["conditionalDeliveryLossRate"] = 0,
                            ["processingLatencyQuantiles"] = q == 0 ? null : JsonSerializer.SerializeToNode(new LatencyQuantiles(0,0,0,0,0,0), Json) }) }).ToArray()) }) }) };
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("\n" + profile.ToJsonString())).ToArray();
            var path = Path.Combine(root, "external.json"); File.WriteAllBytes(path, bytes);
            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, JsonSerializer.Serialize(new { experiment = Options(), ticks = Ticks(), network = Network(),
                mqttYRepository = root, mqttBenchmarkRepository = root, brokerProfilePath = path }, Json));
            var output = Path.Combine(root, "results");
            MqttRelayExperimentCommand.Run(new[] { "--config", configPath, "--output", output });
            var directory = Directory.GetDirectories(output).Single();
            var summary = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "summary.json")))!;
            Assert.AreEqual("completed", summary["status"]!.GetValue<string>());
            Assert.AreEqual(0L, summary["simulated"]!["completed"]!.GetValue<long>(), "External profile fails every publish; embedded profile does not.");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "experiment-manifest.json")))!;
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), manifest["provenance"]!["brokerProfileSha256"]!.GetValue<string>());
        }
        finally { Cleanup(root); }
    }

    private static void Git(string root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, process.StandardError.ReadToEnd());
    }

    [TestMethod]
    public void V3Reference_UsesObservedCompletionFailureAndLatency()
    {
        var root = Temp();
        try
        {
            var profile = CalibrationV3Tests.Calibrate(CalibrationV3Tests.Observations(), .1);
            var options = Options(); options.ClientsPerBroker = 1;
            Mode(options, "TopologyMode", "BrokerFidelity"); Mode(options, "ProfileClientCountMode", "Publishers");
            var result = new MqttRelayExperimentRunner(Ticks(), Network(), new BrokerBenchmarkRepository(profile.ToJsonString()))
                .Run(options, root, new ExperimentProvenance("y", "b", "p"));
            Assert.AreEqual(ExperimentRunStatus.Completed, result.Summary.Status, result.Summary.Error);
            Assert.AreEqual(80d, result.Summary.Reference!.CompletedRps);
            Assert.AreEqual(.2, result.Summary.Reference.PublishFailureRate, 1e-12);
            Assert.AreEqual(9d, result.Summary.Reference.LatencyP99Ms);
        }
        finally { Cleanup(root); }
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void CalibratedFidelity_ReplaysStochasticOutcomesForEveryQos(int qos)
    {
        var root = Temp();
        try
        {
            var calibration = CalibrationV3Tests.Calibrate(CalibrationV3Tests.Observations());
            var options = Options(); options.ClientsPerBroker = 1; options.MeasurementTicks = 2000;
            options.PublishQos = options.SubscribeQos = (MqttQos)qos;
            options.TopologyMode = ExperimentTopologyMode.BrokerFidelity;
            options.ProfileClientCountMode = ProfileClientCountMode.Publishers;
            var runner = new MqttRelayExperimentRunner(Ticks(), Network(), new BrokerBenchmarkRepository(calibration.ToJsonString()));
            var first = runner.Run(options, root, new("y", "b", "p"));
            var second = runner.Run(options, root, new("y", "b", "p"));
            Assert.AreEqual(ExperimentRunStatus.Completed, first.Summary.Status, first.Summary.Error);
            Assert.IsTrue(first.Summary.Simulated.Completed > 0);
            Assert.IsTrue(first.Summary.Simulated.PublishFailed > 0);
            Assert.IsTrue(first.Summary.Simulated.DeliveryDropped > 0);
            Assert.AreEqual(first.Summary.Simulated.Admitted, first.Summary.Simulated.ExpectedDeliveries);
            Assert.AreEqual(JsonSerializer.Serialize(first.Summary), JsonSerializer.Serialize(second.Summary));
            Assert.AreEqual(File.ReadAllText(Path.Combine(first.RunDirectory, "latency-histogram.json")),
                File.ReadAllText(Path.Combine(second.RunDirectory, "latency-histogram.json")));
            if (qos == 0) Assert.IsNull(first.Summary.Simulated.LatencyP99Ms);
        }
        finally { Cleanup(root); }
    }

    [DataTestMethod]
    [DataRow("Publishers", 2)]
    [DataRow("ConnectedClients", 7)]
    public void MultiBrokerRelay_KeepsRemoteForwardersAndActualClientCounts(string mode, int selected)
    {
        var root = Temp();
        try
        {
            var options = Options(); options.Brokers = 2; options.BrokerAssignmentMode = MqttBrokerAssignmentMode.Ideal;
            options.TopologyMode = ExperimentTopologyMode.MqttRelay;
            Mode(options, "ProfileClientCountMode", mode);
            var result = new MqttRelayExperimentRunner(Ticks(), Network()).Run(options, root, new("y", "b", "p"));
            Assert.AreEqual(ExperimentRunStatus.Completed, result.Summary.Status, result.Summary.Error);
            // More broker attempts than the 20 offered source messages proves relay publishing.
            // Connection accounting does not imply that every forwarded topic has a subscriber.
            Assert.IsTrue(result.Summary.Simulated.Attempted > 20);
            Assert.IsTrue(result.Summary.Simulated.Delivered > 0);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RunDirectory, "experiment-manifest.json")));
            var counts = manifest.RootElement.GetProperty("brokerClientCounts").EnumerateArray().ToArray();
            Assert.AreEqual(2, counts.Length);
            foreach (var item in counts)
            {
                Assert.AreEqual(7, item.GetProperty("connectedClients").GetInt32());
                Assert.AreEqual(selected, item.GetProperty("profileClients").GetInt32());
            }
        }
        finally { Cleanup(root); }
    }

    [TestMethod]
    public void CalibrateCommand_WritesSeparateImmutableArtifact()
    {
        var root = Temp();
        try
        {
            Git(root, "init"); Git(root, "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "--allow-empty", "-m", "test");
            var input = Path.Combine(root, "broker-observations.v3.json");
            var sourceBytes = Encoding.UTF8.GetBytes(CalibrationV3Tests.Observations().ToJsonString());
            File.WriteAllBytes(input, sourceBytes);
            var output = Path.Combine(root, "broker-calibration.v3.json");
            var arguments = new[] { "--observations", input, "--output", output, "--mqtty-repository", root, "--rate-rejection-rate", "0.1" };
            Assert.AreEqual(0, MqttRelayExperimentCommand.RunCalibration(arguments));
            var bytes = File.ReadAllBytes(output);
            var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes)).Get("Aedes");
            Assert.AreEqual(1d / 9, profile.For(16, MqttQos.AtLeastOnce, 1).PublishFailureRate, 1e-12);
            Assert.AreEqual(1, MqttRelayExperimentCommand.RunCalibration(arguments), "Existing calibration must not be overwritten.");
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(output));
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(input));
            Assert.AreEqual(1, MqttRelayExperimentCommand.RunCalibration(new[] {
                "--observations", input, "--output", input, "--mqtty-repository", root }));
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(input));
        }
        finally { Cleanup(root); }
    }
    private static void Cleanup(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
    private sealed class Repository : IBrokerBenchmarkRepository
    {
        private readonly MqttBrokerProfile profile;
        public Repository(MqttBrokerProfile profile) => this.profile = profile;
        public MqttBrokerProfile Get(string brokerType) => profile;
    }
}
