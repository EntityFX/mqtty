using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record LoadBalancingOptions
{
    public int SchemaVersion { get; init; } = 1;
    public BalancingMode Mode { get; init; } = BalancingMode.RoundRobin;
    public string BrokerType { get; init; } = "Aedes";
    public int BackendCount { get; init; } = 4;
    public string? BrokerProfilePath { get; init; }
    public ProfileInput[]? BrokerProfiles { get; init; }
    public bool Ideal { get; init; }
    public int TotalPublishers { get; init; } = 64;
    public int PayloadBytes { get; init; } = 16;
    public int PublishQos { get; init; } = 1;
    public int SubscribeQos { get; init; } = 1;
    public double OfferedRps { get; init; } = 100;
    public int Seed { get; init; } = 42;
    public double WarmupSeconds { get; init; } = 1;
    public double MeasurementSeconds { get; init; } = 2;
    public double DrainTimeoutSeconds { get; init; } = 5;
    public TimeSpan TickPeriod { get; init; } = TimeSpan.FromTicks(1000);
    public int NetworkLength { get; init; } = 2;
    public NetworkSettings Network { get; init; } = new();

    public long ToTicks(double seconds)
    {
        var ticks = seconds / TickPeriod.TotalSeconds;
        if (!double.IsFinite(ticks) || ticks < 0 || ticks > long.MaxValue / 4 ||
            Math.Abs(ticks - Math.Round(ticks)) > 1e-6)
            throw new InvalidDataException("Duration must be a nonnegative integral number of model ticks.");
        return checked((long)Math.Round(ticks));
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(typeof(BalancingMode), Mode))
            throw new InvalidDataException("Unsupported schemaVersion or mode.");
        if (!ExperimentInput.BrokerTypes.Contains(BrokerType)) throw new InvalidDataException("Unknown brokerType.");
        if (TotalPublishers is < 1 or > 4096 || BackendCount < 1 || BackendCount > 64 || BackendCount > TotalPublishers ||
            Mode == BalancingMode.Single && BackendCount != 1)
            throw new InvalidDataException("Each backend needs publishers; single requires exactly one backend.");
        if (PayloadBytes is not (16 or 256) || PublishQos is < 0 or > 2 || SubscribeQos is < 0 or > 2)
            throw new InvalidDataException("v1 payload must be 16 or 256 bytes; QoS must be 0, 1 or 2.");
        if (!double.IsFinite(OfferedRps) || OfferedRps <= 0 || !double.IsFinite(WarmupSeconds) ||
            !double.IsFinite(MeasurementSeconds) || MeasurementSeconds <= 0 ||
            !double.IsFinite(DrainTimeoutSeconds) || DrainTimeoutSeconds <= 0 || TickPeriod <= TimeSpan.Zero ||
            NetworkLength is < 1 or > 100)
            throw new InvalidDataException("Invalid rate, duration, tick or networkLength.");
        ToTicks(WarmupSeconds);
        if (ToTicks(MeasurementSeconds) == 0 || ToTicks(DrainTimeoutSeconds) == 0)
            throw new InvalidDataException("Measurement and timeout require at least one tick.");
        if (OfferedRps * MeasurementSeconds >= long.MaxValue / 4 || OfferedRps * WarmupSeconds >= long.MaxValue / 4)
            throw new InvalidDataException("Offered publication count exceeds the supported range.");
        if (OfferedRps * (WarmupSeconds + MeasurementSeconds) > 1000000 || BackendCount * NetworkLength > 1024)
            throw new InvalidDataException("v1 safety budget exceeded: at most 1,000,000 scheduled publications and 1,024 backend network segments per run.");
        if (Network == null) throw new InvalidDataException("network is required.");
        Network.ToOptions().Validate();
    }

    public TicksOptions Ticks() => new()
    {
        TickPeriod = TickPeriod, ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
        OutgoingWaitTicks = 1, CounterHistoryDepth = 100
    };
}
