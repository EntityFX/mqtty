namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Пресет топологии сети.</summary>
public sealed class TopologyPresetDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "line";

    public int ChainLength { get; set; } = 3;

    public int Rays { get; set; } = 3;

    public int RingLength { get; set; } = 4;

    public string BrokerPresetId { get; set; } = string.Empty;

    public int ClientsPerBroker { get; set; } = 2;

    public bool RelayApps { get; set; } = true;

    public string Engine { get; set; } = "relay";

    public int NetworkLength { get; set; } = 2;

    public int Qos { get; set; } = 1;

    public int PayloadBytes { get; set; } = 16;

    public int OfferedRps { get; set; } = 1000;

    public long WarmupTicks { get; set; } = 50000;

    public long MeasurementTicks { get; set; } = 300000;

    public long DrainTimeoutTicks { get; set; } = 100000;

    public int Seed { get; set; } = 42;

    public NetworkTypeDto NetworkType { get; set; } = new();
}
