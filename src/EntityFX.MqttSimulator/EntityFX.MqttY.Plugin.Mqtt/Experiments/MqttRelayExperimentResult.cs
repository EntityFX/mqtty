namespace EntityFX.MqttY.Plugin.Mqtt.Experiments;

public enum ExperimentRunStatus
{
    Completed,
    ConnectionFailed,
    SubscriptionFailed,
    DrainTimedOut,
    Failed
}

public sealed record ExperimentProvenance(
    string MqttYCommitSha,
    string MqttBenchmarkCommitSha,
    string BrokerProfileSha256)
{
    public IReadOnlyList<BrokerProfileArtifact> BrokerProfiles { get; init; } = Array.Empty<BrokerProfileArtifact>();
    public string? ReferenceObservationsSha256 { get; init; }
}

public sealed record BrokerProfileArtifact(string Path, string Sha256, System.Text.Json.JsonElement? Provenance);

public sealed record ExperimentMetricSummary(
    long Attempted,
    long Admitted,
    long Completed,
    long ExpectedDeliveries,
    long Delivered,
    long RateRejected,
    long PublishFailed,
    long DeliveryDropped,
    double CompletedRps,
    double PublishFailureRate,
    double DeliveryLossRate,
    double? LatencyP99Ms);

public sealed record AcceptanceCriterionResult(
    string Name,
    double? Reference,
    double? Simulated,
    double? Error,
    double? Limit,
    string Status);

public sealed record MqttRelayExperimentSummary(
    ExperimentRunStatus Status,
    long MeasurementStartTick,
    long MeasurementEndTick,
    ExperimentMetricSummary Simulated,
    ExperimentMetricSummary? Reference,
    IReadOnlyList<AcceptanceCriterionResult> AcceptanceCriteria,
    string? Error);

public sealed record MqttRelayExperimentResult(
    string RunDirectory,
    MqttRelayExperimentSummary Summary);
