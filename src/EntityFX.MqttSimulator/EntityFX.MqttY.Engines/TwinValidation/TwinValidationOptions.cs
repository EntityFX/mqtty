using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.MqttY.Engines.TwinValidation;

/// <summary>Согласованная одиночная модель; параметры соответствуют ModelTrial статьи 1.</summary>
public sealed class TwinValidationOptions
{
    public string Broker { get; set; } = "Mosquitto";
    public int Qos { get; set; } = 1;
    public int Publishers { get; set; } = 16;
    public int PayloadBytes { get; set; } = 16;
    public int Seed { get; set; } = 42;
    public double WarmupSeconds { get; set; } = 1;
    public double MeasurementSeconds { get; set; } = 2;
    public double DrainSeconds { get; set; } = 5;
    public double TickSeconds { get; set; } = .00001;
    public bool ObserveFullDrainWindow { get; set; } = true;
    public string ProfilePath { get; set; } = "";
    public string ProfileSha256 { get; set; } = "";
    public string RuntimeIdentity { get; set; } = "";

    public long Ticks(double seconds)
    {
        var value = seconds / TickSeconds;
        if (!double.IsFinite(value) || value < 0 || value > long.MaxValue || Math.Abs(value-Math.Round(value)) > 1e-6)
            throw new InvalidDataException("Длительность должна быть точным числом тиков.");
        return checked((long)Math.Round(value));
    }

    public void Validate()
    {
        if (Broker is not ("Mosquitto" or "Aedes" or "ActiveMQ" or "EMQX") || Qos is not (1 or 2) ||
            Publishers <= 0 || PayloadBytes != 16 || !double.IsFinite(TickSeconds) || TickSeconds <= 0 ||
            !double.IsFinite(WarmupSeconds) || WarmupSeconds < 0 ||
            new[]{MeasurementSeconds,DrainSeconds}.Any(x=>!double.IsFinite(x)||x<=0) ||
            string.IsNullOrWhiteSpace(RuntimeIdentity) || string.IsNullOrWhiteSpace(ProfilePath) ||
            ProfileSha256.Length!=64 || ProfileSha256.Any(c=>!Uri.IsHexDigit(c)) || !ObserveFullDrainWindow)
            throw new InvalidDataException("Некорректные параметры одиночной проверки статьи 1.");
        if (Ticks(MeasurementSeconds)==0 || Ticks(DrainSeconds)==0)
            throw new InvalidDataException("Измерение и ожидание должны содержать хотя бы один тик.");
        Ticks(WarmupSeconds);
    }

    public TicksOptions TickOptions() => new() { TickPeriod=TimeSpan.FromSeconds(TickSeconds), OutgoingWaitTicks=1,
        ReceiveWaitPeriod=TimeSpan.FromSeconds(1), CounterHistoryDepth=1 };

    public NetworkOptions NetworkOptions() => new() { NetworkType="eth", TransferTicks=1,
        CapacityBytesPerSecond=100_000_000, ThroughputWindowTicks=1000, QueueCapacity=100_000 };

    public MqttRelayExperimentOptions ExperimentOptions() => new() { RecordPublicationCohort=true,
        ObserveFullDrainWindow=true, TopologyMode=ExperimentTopologyMode.BrokerFidelity, Brokers=1, NetworkLength=1,
        ClientsPerBroker=Publishers, ProfileClientCountMode=ProfileClientCountMode.Publishers,
        PublishQos=(MqttQos)Qos, SubscribeQos=(MqttQos)Qos, PayloadBytes=PayloadBytes,
        BrokerAssignmentMode=Enum.Parse<MqttBrokerAssignmentMode>(Broker), WarmupTicks=Ticks(WarmupSeconds),
        MeasurementTicks=Ticks(MeasurementSeconds), DrainTimeoutTicks=Ticks(DrainSeconds), OfferedRps=Publishers/TickSeconds,
        RandomSeed=Seed, LoadMode=PublishLoadMode.ClosedLoopPerPublisher,
        TransportTiming=TransportTimingMode.EmpiricalServiceBudget, EnableCounters=false, Parallel=false, RefreshStrategy=0 };
}
