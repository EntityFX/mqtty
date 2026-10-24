using System.Text.Json;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;

namespace EntityFX.Tests.Integration.Execution;

[TestClass]
public class EngineLifecycleTests
{
    private static FederationInput Federation(int qos) => FederationInput.Prepare(new FederationOptions
    {
        Brokers = 2, Publishers = 2, Mode = FederationMode.Federation, Topology = "line",
        Qos = qos, WarmupSeconds = .001, MeasurementSeconds = .005, DrainTimeoutSeconds = 1,
        OfferedRps = 1000
    });

    private static PreparedExperiment Balancing(int qos)
    {
        var options = new LoadBalancingOptions
        {
            Ideal = true, TotalPublishers = 2, BackendCount = 2, NetworkLength = 1,
            PublishQos = qos, SubscribeQos = qos, WarmupSeconds = .001,
            MeasurementSeconds = .005, DrainTimeoutSeconds = 1, OfferedRps = 1000
        };
        return ExperimentInput.Prepare(options,
            new InputSnapshot("lifecycle-test", JsonSerializer.SerializeToUtf8Bytes(options, ExperimentInput.Json)));
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public void ProgressObservers_PreserveScientificResults(int qos)
    {
        var federation = Federation(qos);
        var expected = new FederationRunner().Run(federation);
        var phases = new HashSet<string>();
        var actual = new FederationRunner().Run(federation, onProgress: (graph, phase) =>
        {
            Assert.IsTrue(graph.TotalTicks > 0);
            phases.Add(phase);
        });
        Assert.AreEqual("completed", actual.Status, actual.Error);
        Assert.AreEqual(JsonSerializer.Serialize(expected, FederationInput.Json), JsonSerializer.Serialize(actual, FederationInput.Json));
        Assert.IsTrue(phases.Contains("measure"));

        var balancing = Balancing(qos);
        var baseline = new LoadBalancingExperimentRunner().RunInMemory(balancing);
        phases.Clear();
        var observed = new LoadBalancingExperimentRunner().RunInMemory(balancing, onProgress: (graph, phase) => phases.Add(phase));
        Assert.AreEqual("completed", observed.Status, observed.Error);
        Assert.AreEqual(JsonSerializer.Serialize(baseline, ExperimentInput.Json), JsonSerializer.Serialize(observed, ExperimentInput.Json));
        Assert.IsTrue(phases.Contains("measure"));
    }

    [TestMethod]
    public void FederationCancellation_RetainsSealedPartialEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        var output = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mqtty-cancellation-" + Guid.NewGuid()));
        try
        {
            var result = new FederationRunner().Run(Federation(1), output, cancellationToken: cancellation.Token,
                onProgress: (_, phase) => { if (phase == "measure") cancellation.Cancel(); });
            Assert.AreEqual("failed", result.Status);
            StringAssert.Contains(result.Error!, nameof(OperationCanceledException));
            Assert.IsTrue(result.Phases.Any(p => p.Name == "measure" && p.Status == "failed"));
            Assert.IsTrue(File.Exists(Path.Combine(output, "sha256.json")));
            Assert.IsTrue(File.Exists(Path.Combine(output, "delivery-receipts.json")));
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [TestMethod]
    public void BalancingCancellation_StopsDuringMeasurement()
    {
        using var cancellation = new CancellationTokenSource();
        var result = new LoadBalancingExperimentRunner().RunInMemory(Balancing(1), cancellation.Token,
            (_, phase) => { if (phase == "measure") cancellation.Cancel(); });
        Assert.AreEqual("failed", result.Status);
        StringAssert.Contains(result.Error!, nameof(OperationCanceledException));
        Assert.IsTrue(result.Phases.Any(p => p.Phase == "measure" && p.Status == "failed"));
    }

    [TestMethod]
    public void AlreadyCancelled_RecordsFailureBeforeConstructingTopology()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var f = new FederationRunner().Run(Federation(1), cancellationToken: cancellation.Token);
        var b = new LoadBalancingExperimentRunner().RunInMemory(Balancing(1), cancellation.Token);
        Assert.AreEqual("failed", f.Status);
        Assert.AreEqual("failed", b.Status);
        Assert.IsTrue(f.Phases.All(p => p.StartTick == 0 && p.EndTick == 0));
        Assert.IsTrue(b.Phases.All(p => p.StartTick == 0 && p.EndTick == 0));
        StringAssert.Contains(f.Error!, nameof(OperationCanceledException));
        StringAssert.Contains(b.Error!, nameof(OperationCanceledException));
    }
}
