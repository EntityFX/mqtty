using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record NetworkSettings
{
    public double CapacityBytesPerSecond { get; init; } = 125000000;
    public int ThroughputWindowTicks { get; init; } = 100;
    public int QueueCapacity { get; init; } = 100000;
    public int TransferTicks { get; init; } = 2;
    public NetworkOptions ToOptions() => new()
    {
        CapacityBytesPerSecond = CapacityBytesPerSecond, ThroughputWindowTicks = ThroughputWindowTicks,
        QueueCapacity = QueueCapacity, TransferTicks = TransferTicks, NetworkType = "ethernet"
    };
}
