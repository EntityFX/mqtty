using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Experiments;

public enum ExperimentTopologyMode { BrokerFidelity, MqttRelay }
public enum ProfileClientCountMode { Publishers, ConnectedClients }

public enum MqttBrokerAssignmentMode
{
    Ideal,
    Mosquitto,
    ActiveMQ,
    Aedes,
    EMQX,
    Mixed
}

public sealed class MqttRelayExperimentOptions
{
    public ExperimentTopologyMode TopologyMode { get; set; } = ExperimentTopologyMode.MqttRelay;
    public ProfileClientCountMode ProfileClientCountMode { get; set; } = ProfileClientCountMode.ConnectedClients;
    public int Brokers { get; set; }
    public int NetworkLength { get; set; }
    public int ClientsPerBroker { get; set; }
    public MqttQos PublishQos { get; set; }
    public MqttQos SubscribeQos { get; set; }
    public int PayloadBytes { get; set; }
    public MqttBrokerAssignmentMode BrokerAssignmentMode { get; set; } = MqttBrokerAssignmentMode.Ideal;
    public IReadOnlyList<string> MixedBrokerTypes { get; set; } = Array.Empty<string>();
    public long WarmupTicks { get; set; }
    public long MeasurementTicks { get; set; }
    public long DrainTimeoutTicks { get; set; }
    public double OfferedRps { get; set; }
    public int RandomSeed { get; set; }
    public bool Parallel { get; set; }
    public int RefreshStrategy { get; set; }
    public bool EnableCounters { get; set; }
    public bool FidelityMode { get; set; } = true;

    public void Validate()
    {
        if (!Enum.IsDefined(typeof(ExperimentTopologyMode), TopologyMode) ||
            !Enum.IsDefined(typeof(ProfileClientCountMode), ProfileClientCountMode))
            throw new InvalidDataException("Unknown topology or profile client-count mode.");
        if (Brokers <= 0 || NetworkLength <= 0 || ClientsPerBroker <= 0 || PayloadBytes <= 0)
            throw new InvalidDataException(
                "Brokers, NetworkLength, ClientsPerBroker and PayloadBytes must be positive.");
        if (!Enum.IsDefined(typeof(MqttQos), PublishQos) ||
            !Enum.IsDefined(typeof(MqttQos), SubscribeQos))
            throw new InvalidDataException("PublishQos and SubscribeQos must be valid MQTT QoS values.");
        if (!Enum.IsDefined(typeof(MqttBrokerAssignmentMode), BrokerAssignmentMode))
            throw new InvalidDataException("BrokerAssignmentMode must be a known assignment mode.");
        if (WarmupTicks < 0 || MeasurementTicks <= 0 || DrainTimeoutTicks <= 0)
            throw new InvalidDataException(
                "WarmupTicks must be non-negative; MeasurementTicks and DrainTimeoutTicks must be positive.");
        if (!double.IsFinite(OfferedRps) || OfferedRps <= 0)
            throw new InvalidDataException("OfferedRps must be finite and positive.");
        if (RefreshStrategy < 0) throw new InvalidDataException("RefreshStrategy must be non-negative.");
        if (Parallel && FidelityMode)
            throw new InvalidDataException("Fidelity runs must use sequential refresh.");

        if (BrokerAssignmentMode == MqttBrokerAssignmentMode.Mixed)
        {
            if (MixedBrokerTypes == null || MixedBrokerTypes.Count != Brokers)
                throw new InvalidDataException(
                    "MixedBrokerTypes must contain exactly one broker type per broker area.");
            foreach (var broker in MixedBrokerTypes)
                ValidateBrokerName(broker);
        }
        else if (MixedBrokerTypes?.Count > 0)
        {
            throw new InvalidDataException("MixedBrokerTypes is only valid in Mixed assignment mode.");
        }
    }

    public int ResolveProfileClientCount() => ProfileClientCountMode == ProfileClientCountMode.Publishers
        ? ClientsPerBroker : ClientsPerBroker + (TopologyMode == ExperimentTopologyMode.BrokerFidelity ? 1 : Brokers + 3);

    public IReadOnlyList<string?> ResolveBrokerTypes()
    {
        Validate();
        if (BrokerAssignmentMode == MqttBrokerAssignmentMode.Ideal)
            return Enumerable.Repeat<string?>(null, Brokers).ToArray();
        if (BrokerAssignmentMode == MqttBrokerAssignmentMode.Mixed)
            return MixedBrokerTypes.Cast<string?>().ToArray();
        return Enumerable.Repeat<string?>(BrokerAssignmentMode.ToString(), Brokers).ToArray();
    }

    private static void ValidateBrokerName(string broker)
    {
        if (broker is not ("Mosquitto" or "ActiveMQ" or "Aedes" or "EMQX"))
            throw new InvalidDataException($"Unknown broker assignment '{broker}'.");
    }
}
