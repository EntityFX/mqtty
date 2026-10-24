using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Diagnostics;
using EntityFX.MqttY.Contracts.Network.Diagnostics;
using EntityFX.MqttY.Federation;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Regression)]
public class FederationDiagnosticsTests
{
    private static string NewOutput() => Path.Combine(Path.GetTempPath(), "mqtty-drain-diagnostics-" + Guid.NewGuid());
    private static void Remove(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (absolute.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(absolute).StartsWith("mqtty-drain-diagnostics-", StringComparison.Ordinal) && Directory.Exists(absolute))
            Directory.Delete(absolute, true);
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public void Diagnostics_DoNotChangeScientificFiles_AndAreSealed(int qos)
    {
        var plain = NewOutput(); var diagnostic = NewOutput();
        try
        {
            var input = FederationInput.Prepare(new FederationOptions
            { Brokers = 4, Publishers = 4, Topology = "star", Qos = qos, WarmupSeconds = .001, MeasurementSeconds = .004 });
            var a = new FederationRunner().Run(input, plain);
            var b = new FederationRunner().Run(input, diagnostic, diagnostics: true);
            Assert.AreEqual("completed", a.Status, a.Error); Assert.AreEqual("completed", b.Status, b.Error);
            // Every previous artifact except the seal must remain byte identical, including manifest/config/GraphML.
            foreach (var file in Directory.GetFiles(plain, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(plain, file);
                if (relative == "sha256.json") continue;
                CollectionAssert.AreEqual(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(diagnostic, relative)), relative);
            }
            Assert.IsFalse(File.Exists(Path.Combine(plain, "drain-diagnostics.json")));
            var path = Path.Combine(diagnostic, "drain-diagnostics.json");
            var diagnostics = JsonSerializer.Deserialize<FederationDrainDiagnostics>(File.ReadAllText(path), FederationInput.Json)!;
            Assert.AreEqual(4, diagnostics.Snapshots.Count);
            Assert.AreEqual(5, diagnostics.Snapshots.Last().Networks.Length, "All sites plus transit must be recorded.");
            Assert.IsTrue(diagnostics.Snapshots.Last().IsIdle);
            Assert.IsTrue(diagnostics.Snapshots.Last().Networks.All(n => n.Queued == 0 && n.Counters.ContainsKey("Refused")));
            Assert.IsTrue(diagnostics.Snapshots.Last().Nodes.All(n => n.Mqtt == null || n.Mqtt.TotalPendingMessages == 0));
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(diagnostic, "sha256.json")))!;
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), hashes["drain-diagnostics.json"]);
        }
        finally { Remove(plain); Remove(diagnostic); }
    }

    [TestMethod]
    public void PendingCapture_IsReadOnly_AndLimitsDoNotHideTotals()
    {
        var options = new FederationOptions { Brokers = 1, Publishers = 40, Topology = "line", Mode = FederationMode.Single,
            WarmupSeconds = 0, MeasurementSeconds = .001, Qos = 1 };
        var ledger = new FederationDeliveryLedger(options);
        using var topology = FederationTopologyBuilder.Build(FederationInput.Prepare(options), ledger, new TransmissionLedger(ledger));
        void Until(Func<bool> condition)
        {
            for (var i = 0; i < 100 && !condition(); i++) topology.Graph.Refresh(false, 0);
            Assert.IsTrue(condition());
        }
        foreach (var c in topology.Connections) c.Client.BeginConnect(topology.Brokers[c.Target].Name, true);
        Until(() => topology.Graph.Clients.Values.All(c => c.IsConnected));
        foreach (var s in topology.Subscriptions) s.Client.BeginSubscribe(s.Filter, MqttQos.AtLeastOnce);
        Until(() => topology.Subscriptions.All(s => s.Client.IsSubscribed(s.Filter)) && topology.IsIdle);
        var publication = ledger.Create(0, topology.Graph.TotalTicks, true);
        topology.Publishers[0].Publish(FederationTopics.Delivered(0), publication.Payload, MqttQos.AtLeastOnce);
        var publisher = (IMqttPendingDiagnostics)topology.Publishers[0];
        var tick = topology.Graph.TotalTicks;
        var queues = ((INodeQueueDiagnostics)topology.Publishers[0]).GetQueueSnapshot();
        var complete = publisher.GetPendingSnapshot(); var capped = publisher.GetPendingSnapshot(0, 0);
        Assert.AreEqual(1, complete.TotalPendingMessages);
        Assert.AreEqual(1, complete.Messages.Length);
        Assert.AreEqual(1, complete.MessageStates["PendingToAcknowledge"]);
        Assert.AreEqual(1, complete.TotalSessions);
        Assert.AreEqual(complete.TotalPendingMessages, capped.TotalPendingMessages);
        Assert.AreEqual(complete.TotalSessions, capped.TotalSessions);
        Assert.AreEqual(0, capped.Sessions.Length); Assert.AreEqual(0, capped.Messages.Length); Assert.IsTrue(capped.Truncated);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(publication.Payload)).ToLowerInvariant(), complete.Messages[0].PayloadSha256);
        Assert.AreEqual(tick, topology.Graph.TotalTicks);
        Assert.AreEqual(queues, ((INodeQueueDiagnostics)topology.Publishers[0]).GetQueueSnapshot());
        Assert.AreEqual(JsonSerializer.Serialize(complete), JsonSerializer.Serialize(publisher.GetPendingSnapshot()));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => publisher.GetPendingSnapshot(1025));
        var snapshots = new FederationDrainDiagnostics();
        snapshots.Capture(topology, "drain", "entry"); snapshots.Capture(topology, "drain", "entry");
        Assert.AreEqual(JsonSerializer.Serialize(snapshots.Snapshots[0]), JsonSerializer.Serialize(snapshots.Snapshots[1]));
        Assert.AreEqual(tick, topology.Graph.TotalTicks);
        for (var p = 1; p < options.Publishers; p++)
        {
            var next = ledger.Create(p, topology.Graph.TotalTicks, true);
            topology.Publishers[p].Publish(FederationTopics.Delivered(0), next.Payload, MqttQos.AtLeastOnce);
        }
        var broker = (IMqttPendingDiagnostics)topology.Brokers[0];
        Until(() => broker.GetPendingSnapshot(0, 0).TotalPendingMessages == 40);
        var bounded = broker.GetPendingSnapshot(3, 2);
        Assert.AreEqual(40, bounded.TotalPendingMessages);
        Assert.AreEqual(40, bounded.MessageStates["PendingToAcknowledge"]);
        Assert.AreEqual(41, bounded.TotalSessions);
        Assert.AreEqual(3, bounded.Messages.Length); Assert.AreEqual(2, bounded.Sessions.Length); Assert.IsTrue(bounded.Truncated);
    }

    [TestMethod]
    public void DrainTimeout_PreservesPartialDiagnostics_AndExactCounters()
    {
        var output = NewOutput();
        try
        {
            var options = new FederationOptions { Brokers = 4, Publishers = 64, Topology = "star", Qos = 1,
                OfferedRps = 5000, WarmupSeconds = 0, MeasurementSeconds = .04, DrainTimeoutSeconds = .003, QueueCapacity = 16 };
            var result = new FederationRunner().Run(FederationInput.Prepare(options), output, diagnostics: true);
            Assert.AreEqual("failed", result.Status); StringAssert.Contains(result.Error!, "drain timed out");
            var data = JsonSerializer.Deserialize<FederationDrainDiagnostics>(File.ReadAllText(Path.Combine(output, "drain-diagnostics.json")), FederationInput.Json)!;
            var entry = data.Snapshots.Single(s => s.Phase == "drain" && s.Event == "entry");
            var failed = data.Snapshots.Single(s => s.Phase == "drain" && s.Event == "failed");
            Assert.AreEqual(options.ToTicks(options.DrainTimeoutSeconds), failed.Tick - entry.Tick);
            Assert.IsFalse(failed.IsIdle); Assert.AreEqual(16, failed.FederatorTargets.Length);
            Assert.IsTrue(failed.FederatorTargets.Any(s => s.Queued > 0 || s.HasPendingPublish));
            Assert.IsTrue(failed.Nodes.Any(s => s.Mqtt?.TotalPendingMessages > 0));
            Assert.IsTrue(failed.Nodes.All(s => s.Mqtt == null || s.Mqtt.Messages.Length + s.Mqtt.Acknowledgements.Length <= 32));
            Assert.IsTrue(failed.Nodes.All(s => s.Mqtt == null || s.Mqtt.Sessions.Length <= 64));
        }
        finally { Remove(output); }
    }

    [TestMethod]
    public void Cli_AcceptsDiagnosticRun_RejectsDuplicateFlag_AndLeavesConfigUnchanged()
    {
        var basePath = NewOutput(); Directory.CreateDirectory(basePath);
        try
        {
            var config = Path.Combine(basePath, "config.json");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new FederationOptions { MeasurementSeconds = .002, WarmupSeconds = 0 }, FederationInput.Json);
            File.WriteAllBytes(config, bytes);
            var stdout = new StringWriter(); var stderr = new StringWriter();
            Assert.AreEqual(0, FederationCommand.Execute(new[] { "run", "--config", config, "--output", Path.Combine(basePath, "run"), "--diagnostics" }, stdout, stderr), stderr.ToString());
            Assert.IsTrue(File.Exists(Path.Combine(basePath, "run", "drain-diagnostics.json")));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(config));
            Assert.AreEqual(1, FederationCommand.Execute(new[] { "run", "--config", config, "--output", Path.Combine(basePath, "rejected"), "--diagnostics", "--diagnostics" }, stdout, stderr));
            Assert.IsFalse(Directory.Exists(Path.Combine(basePath, "rejected")));
        }
        finally { Remove(basePath); }
    }
}
