namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Статус узла сети для диалога статуса брокера и мониторинга.</summary>
public sealed class NodeStatusSnapshot
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Specification { get; init; } = string.Empty;

    public string? Network { get; init; }

    public bool Connected { get; init; }

    public long QueueLength { get; init; }

    public IReadOnlyDictionary<string, long> Counters { get; init; } = new Dictionary<string, long>();
}
