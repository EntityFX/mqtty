namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Параметры модельной сети.</summary>
public sealed class NetworkTypeDto
{
    public string Name { get; set; } = "ethernet";

    public long CapacityBytesPerSecond { get; set; } = 100000000;

    public int ThroughputWindowTicks { get; set; } = 100;

    public long QueueCapacity { get; set; } = 100000;

    public int TransferTicks { get; set; } = 1;
}
