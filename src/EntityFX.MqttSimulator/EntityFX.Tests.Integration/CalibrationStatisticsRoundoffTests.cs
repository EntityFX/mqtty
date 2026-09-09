using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration;

[TestClass]
public class CalibrationStatisticsRoundoffTests
{
    [TestMethod]
    public void Calibration_AcceptsActualCampaignAggregatorJson_WithoutChangingCalculatedValues()
    {
        using var stream = typeof(CalibrationStatisticsRoundoffTests).Assembly.GetManifestResourceStream(
            "EntityFX.Tests.Integration.Fixtures.campaign-aggregator-roundoff-point.json")!;
        using var reader = new StreamReader(stream);
        var point = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        Assert.AreEqual(.10000000000000002, point["attemptedRps"]!["mean"]!.GetValue<double>());
        Assert.AreEqual(.1, point["attemptedRps"]!["max"]!.GetValue<double>());
        var input = CalibrationV3Tests.Observations();
        input["points"]![4] = point; // Replace matching Aedes/16/QoS1/one-publisher point; keep every calculated field.
        var output = CalibrationV3Tests.Calibrate(input);
        var sample = new BrokerBenchmarkRepository(output.ToJsonString()).Get("Aedes").For(16, MqttQos.AtLeastOnce, 1);
        Assert.AreEqual(.10000000000000002, sample.AttemptedRps);
        Assert.AreEqual(.09000000000000001, sample.TargetCompletedRps);
        Assert.AreEqual(.10000000000000002, sample.TargetPublishFailureRate);
        Assert.AreEqual(.10000000000000002, sample.ObservedLatencyQuantiles!.P99Ms);
    }

    [DataTestMethod]
    [DataRow(1e-100, true)]
    [DataRow(1e-100, false)]
    [DataRow(.1, true)]
    [DataRow(.1, false)]
    [DataRow(.9, true)]
    [DataRow(.9, false)]
    public void Calibration_AcceptsOneRepresentableStepOutsideMeanBounds(double bound, bool above)
    {
        var input = WithFailureStatistics(bound, above ? Math.BitIncrement(bound) : Math.BitDecrement(bound));
        CalibrationV3Tests.Calibrate(input);
    }

    [DataTestMethod]
    [DataRow(1e-100, true)]
    [DataRow(1e-100, false)]
    [DataRow(.1, true)]
    [DataRow(.1, false)]
    [DataRow(.9, true)]
    [DataRow(.9, false)]
    public void Calibration_RejectsMaterialMeanBoundViolationAtEveryScale(double bound, bool above)
    {
        var input = WithFailureStatistics(bound, bound * (above ? 1.000001 : .999999));
        Assert.ThrowsException<InvalidDataException>(() => CalibrationV3Tests.Calibrate(input));
    }

    [TestMethod]
    public void Calibration_StillRejectsNonfiniteStatistics()
    {
        var input = WithFailureStatistics(.1, .1);
        input["points"]![0]!["publishFailureRate"]!["mean"] = JsonNode.Parse("1e999");
        Assert.ThrowsException<InvalidDataException>(() => CalibrationV3Tests.Calibrate(input));
    }

    [TestMethod]
    public void Calibration_StillRejectsInvertedMinMaxEvenWithinRoundoffTolerance()
    {
        var input = WithFailureStatistics(.1, .1);
        input["points"]![0]!["publishFailureRate"]!["min"] = Math.BitIncrement(.1);
        Assert.ThrowsException<InvalidDataException>(() => CalibrationV3Tests.Calibrate(input));
    }

    [TestMethod]
    public void Calibration_LargeMaximumDoesNotMaskMeanBelowTinyPositiveMinimum()
    {
        var input = WithFailureStatistics(1e-100, 0);
        input["points"]![0]!["publishFailureRate"]!["max"] = .9;
        Assert.ThrowsException<InvalidDataException>(() => CalibrationV3Tests.Calibrate(input));
    }

    private static JsonObject WithFailureStatistics(double bound, double mean)
    {
        var input = CalibrationV3Tests.Observations();
        input["points"]![0]!["publishFailureRate"] = new JsonObject {
            ["mean"] = mean, ["standardDeviation"] = 0, ["ci95HalfWidth"] = 0,
            ["ci95Lower"] = mean, ["ci95Upper"] = mean, ["min"] = bound, ["max"] = bound, ["count"] = 3 };
        return input;
    }
}
