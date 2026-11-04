using EntityFX.MqttY.Contracts.Options;
namespace EntityFX.MqttY.Federation;

public sealed record FederationNetworkOptions
{
    public long CapacityBytesPerSecond { get; init; } = 125000000;
    public int ThroughputWindowTicks { get; init; } = 100;
    public int QueueCapacity { get; init; } = 100000;
    public int TransferTicks { get; init; } = 2;
    public NetworkOptions Build() => new()
    {
        CapacityBytesPerSecond = CapacityBytesPerSecond,
        ThroughputWindowTicks = ThroughputWindowTicks,
        QueueCapacity = QueueCapacity,
        TransferTicks = TransferTicks
    };
}
