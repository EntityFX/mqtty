using System.Text.Json;
using EntityFX.MqttY.LoadBalancing;

namespace EntityFX.Tests.Integration;

[TestClass]
public class LoadBalancingTimingTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OptionalTiming_PreservesScientificSummaryAndCountsEveryMeasuredTick(bool parallel)
    {
        var folder = Path.Combine(Path.GetTempPath(), "mqtty-timing-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var options = new LoadBalancingOptions { Ideal = true, BackendCount = 4, TotalPublishers = 64,
                PayloadBytes = 256, OfferedRps = 4000, WarmupSeconds = 0, MeasurementSeconds = .01,
                DrainTimeoutSeconds = 1 };
            var input = ExperimentInput.Prepare(options, new InputSnapshot(Path.Combine(folder, "input.json"),
                JsonSerializer.SerializeToUtf8Bytes(options, ExperimentInput.Json)));
            var runner = new LoadBalancingExperimentRunner();
            var plain = runner.Run(input, Path.Combine(folder, "plain"), parallel, 0);
            var measured = runner.RunMeasured(input, Path.Combine(folder, "measured"), parallel, 0);
            Assert.AreEqual("completed", plain.Status);
            Assert.AreEqual(JsonSerializer.Serialize(plain, ExperimentInput.Json),
                JsonSerializer.Serialize(measured.Summary, ExperimentInput.Json));
            Assert.AreEqual(100L, measured.Timing.RefreshCallsByPhase["measure"]);
            Assert.IsTrue(measured.Timing.RefreshWallSeconds > 0);
            Assert.IsTrue(measured.Timing.RunnerWallSeconds >= measured.Timing.RefreshWallSeconds);
            Assert.IsTrue(measured.Timing.PhaseWallSeconds["measure"] >= measured.Timing.RefreshWallSecondsByPhase["measure"]);
            Assert.IsTrue(measured.Timing.ExportWallSeconds > 0);
        }
        finally { Directory.Delete(folder, true); }
    }
}
