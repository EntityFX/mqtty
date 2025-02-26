namespace EntityFX.MqttY.Contracts.Monitoring;

public class MonitoringScope : IMonitoringItem
{
    public Guid Id { get; init; }

    public string ScopeLabel { get; init; } = string.Empty;

    public int Level { get; init; }

    public DateTimeOffset Date { get; init; }

    public MonitoringScope? Parent { get; init; } = null;

    public List<IMonitoringItem> Items { get; init; } = new List<IMonitoringItem>();

    public MonitoringItemType MonitoringItemType => MonitoringItemType.Scope;

    public ScopeStatus ScopeStatus { get; set; } = ScopeStatus.Begin;

    public long StartTick { get; init; }
    public long EndTick { get; set; }
    public long Ticks { get; set; }

    public string? Source { get; set; }
    public string? Destination { get; set; }

}
