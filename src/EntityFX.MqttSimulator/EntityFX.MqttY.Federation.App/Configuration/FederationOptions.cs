using EntityFX.MqttY.Contracts.Options;
namespace EntityFX.MqttY.Federation;

public sealed record FederationOptions
{
    public int SchemaVersion { get; init; } = 1;
    public FederationMode Mode { get; init; } = FederationMode.Federation;
    public int Brokers { get; init; } = 3;
    public string Topology { get; init; } = "ring";
    public SiteLink[]? Links { get; init; }
    public FederationPhysicalLayout? PhysicalLayout { get; init; }
    public int[]? PublisherSites { get; init; }
    public int Publishers { get; init; } = 12;
    public int[]? ReceiverSites { get; init; }
    public int PayloadBytes { get; init; } = 16;
    public int Qos { get; init; } = 1;
    public double OfferedRps { get; init; } = 1000;
    public int Seed { get; init; } = 42;
    public double WarmupSeconds { get; init; } = .01;
    public double MeasurementSeconds { get; init; } = .2;
    public double DrainTimeoutSeconds { get; init; } = 1;
    public TimeSpan TickPeriod { get; init; } = TimeSpan.FromTicks(1000);
    public int QueueCapacity { get; init; } = 1024;
    public int CacheCapacity { get; init; } = 100000;
    public int? HopLimit { get; init; }
    public bool Ideal { get; init; } = true;
    public string BrokerType { get; init; } = "Mosquitto";
    public string? BrokerProfilePath { get; init; }
    public FederationNetworkOptions Network { get; init; } = new();
    public int[] Receivers() => ReceiverSites ?? Enumerable.Range(0, Brokers).ToArray();
    public int PublisherSite(int publisher) => PublisherSites?[publisher] ?? publisher % Brokers;
    public int NetworkForSite(int site) => PhysicalLayout?.BrokerNetworks[site] ?? site;
    public int BrokerForSite(int site) => Mode == FederationMode.Single ? 0 : site;
    public long ToTicks(double seconds)
    {
        var value = seconds / TickPeriod.TotalSeconds;
        if (!double.IsFinite(value) || value < 0 || value > 10000000 || Math.Abs(value - Math.Round(value)) > 1e-6)
            throw new InvalidDataException("Duration must be a bounded integral number of ticks.");
        return checked((long)Math.Round(value));
    }
    public TicksOptions Ticks() => new()
    {
        TickPeriod = TickPeriod,
        ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
        OutgoingWaitTicks = 1,
        CounterHistoryDepth = 100
    };
    public void Validate()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(typeof(FederationMode), Mode) || Brokers is < 1 or > 16 ||
            Publishers < Brokers || Publishers > 4096 || Qos is < 0 or > 2 || PayloadBytes is not (16 or 256))
            throw new InvalidDataException("Invalid mode, brokers, publishers, QoS or payload.");
        if (TickPeriod <= TimeSpan.Zero || !double.IsFinite(OfferedRps) || OfferedRps <= 0 ||
            !double.IsFinite(WarmupSeconds) || WarmupSeconds < 0 || !double.IsFinite(MeasurementSeconds) || MeasurementSeconds <= 0 ||
            !double.IsFinite(DrainTimeoutSeconds) || DrainTimeoutSeconds <= 0 || ToTicks(MeasurementSeconds) < 1 || ToTicks(DrainTimeoutSeconds) < 1)
            throw new InvalidDataException("Invalid timing or rate.");
        ToTicks(WarmupSeconds);
        var publications = Math.Ceiling(OfferedRps * WarmupSeconds) + Math.Ceiling(OfferedRps * MeasurementSeconds);
        if (publications > 100000 || CacheCapacity < publications || CacheCapacity > 1000000 || QueueCapacity is < 1 or > 100000 ||
            (HopLimit ?? Brokers) < 1 || (HopLimit ?? Brokers) > 64 || publications * Receivers().Length > 1000000)
            throw new InvalidDataException("Bounded experiment/cache/queue/hop budget exceeded.");
        if (Receivers().Distinct().Count() != Receivers().Length || Receivers().Any(site => site < 0 || site >= Brokers))
            throw new InvalidDataException("Invalid receiver sites.");
        if (Ideal != (BrokerProfilePath == null) || BrokerType is not ("Mosquitto" or "Aedes" or "ActiveMQ" or "EMQX"))
            throw new InvalidDataException("Use ideal with no profile, or an explicit supported broker profile.");
        if (Network == null) throw new InvalidDataException("Network is required.");
        Network.Build().Validate();
        if (PublisherSites != null && (PublisherSites.Length != Publishers || PublisherSites.Any(site => site < 0 || site >= Brokers)))
            throw new InvalidDataException("Invalid publisher placement.");
        if (PhysicalLayout is { } layout && (Topology != "custom" || layout.NetworkNames.Length is < 1 or > 64 ||
            layout.NetworkNames.Any(string.IsNullOrWhiteSpace) || layout.NetworkNames.Distinct().Count() != layout.NetworkNames.Length ||
            layout.BrokerNetworks.Length != Brokers || layout.BrokerNetworks.Distinct().Count() != Brokers ||
            layout.BrokerNetworks.Any(n => n < 0 || n >= layout.NetworkNames.Length)))
            throw new InvalidDataException("Invalid physical graph layout.");
        if (PhysicalLayout is { } identities &&
            (identities.BrokerNames != null && (identities.BrokerNames.Length != Brokers || identities.BrokerNames.Any(string.IsNullOrWhiteSpace) || identities.BrokerNames.Distinct().Count() != Brokers) ||
             identities.PublisherNames != null && (identities.PublisherNames.Length != Publishers || identities.PublisherNames.Any(string.IsNullOrWhiteSpace) || identities.PublisherNames.Distinct().Count() != Publishers)))
            throw new InvalidDataException("Invalid physical graph node identities.");
        FederationGraph.Edges(this);
    }
}
