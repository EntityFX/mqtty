namespace EntityFX.MqttY.Contracts.NetworkLogger;

public class NetworkLoggerScope : INetworkLoggerItem
{
    public Guid Id { get; init; }

    public string ScopeLabel { get; init; } = string.Empty;

    public int Level { get; init; }

    public DateTimeOffset Date { get; init; }

    public NetworkLoggerScope? Parent { get; init; } = null;

    public List<INetworkLoggerItem> Items { get; init; } = new List<INetworkLoggerItem>();

    public NetworkLoggerItemType ItemType => NetworkLoggerItemType.Scope;

    public ScopeStatus ScopeStatus { get; set; } = ScopeStatus.Begin;

    public long StartTick { get; init; }
    public long EndTick { get; set; }
    public long Ticks { get; set; }

    public string? Source { get; set; }
    public string? Destination { get; set; }

}
