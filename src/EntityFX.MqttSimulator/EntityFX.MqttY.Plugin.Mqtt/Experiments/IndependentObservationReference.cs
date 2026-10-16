using System.Text.Json;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.Experiments;

/// <summary>Validates observed statistics and selects a reference; never changes model parameters.</summary>
public static class IndependentObservationReference
{
    public static void EnsureIndependent(byte[] bytes, IReadOnlyList<BrokerProfileArtifact> artifacts, string? coverageProtocol = null)
    {
        using var document = JsonDocument.Parse(bytes);
        var identity = document.RootElement.GetProperty("provenance").GetProperty("identity");
        foreach (var artifact in artifacts)
        {
            if (artifact.Provenance == null) throw new InvalidDataException("Independent comparison requires calibrated v3 provenance.");
            var declared = artifact.Provenance.Value.TryGetProperty("coverageProtocol", out var protocol) ? protocol.GetString() : null;
            if (declared != coverageProtocol) throw new InvalidDataException("Independent reference coverage differs from the calibrated profile.");
            var training = artifact.Provenance.Value.GetProperty("observations").GetProperty("identity");
            if (identity.GetProperty("campaignId").GetString() == training.GetProperty("campaignId").GetString())
                throw new InvalidDataException("Independent validation must use a distinct real campaign.");
            foreach (var key in new[] { "cpuMode", "deploymentMode", "standSha256", "mqttBenchmarkCommitSha" })
                if (identity.GetProperty(key).GetString() != training.GetProperty(key).GetString())
                    throw new InvalidDataException($"Independent reference has different {key}.");
        }
    }
    public static ExperimentMetricSummary Read(byte[] bytes, string broker,
        MqttRelayExperimentOptions options, string commit, string? coverageProtocol = null)
    {
        _ = BrokerCalibrationV3.Calibrate(bytes, commit, broker: broker, coverageProtocol: coverageProtocol);
        using var document = JsonDocument.Parse(bytes);
        var point = document.RootElement.GetProperty("points").EnumerateArray().Single(point =>
            point.GetProperty("broker").GetString() == broker &&
            point.GetProperty("messageBytes").GetInt32() == options.PayloadBytes &&
            point.GetProperty("qos").GetInt32() == (int)options.PublishQos &&
            point.GetProperty("publishers").GetInt32() == options.ClientsPerBroker);
        double Mean(string name) => point.GetProperty(name).GetProperty("mean").GetDouble();
        double? p99 = (int)options.PublishQos == 0 ? null :
            point.GetProperty("observedLatencyQuantiles").GetProperty("p99Ms").GetDouble();
        return new ExperimentMetricSummary(0, 0, 0, 0, 0, 0, 0, 0, Mean("completedRps"),
            Mean("publishFailureRate"), Mean("deliveryLossRate"), p99);
    }
}
