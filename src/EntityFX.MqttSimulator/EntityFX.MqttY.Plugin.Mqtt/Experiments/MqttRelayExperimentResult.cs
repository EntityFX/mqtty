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
    string BrokerProfileSha256);

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
