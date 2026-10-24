namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Снимок прогресса симуляции для мониторинга.</summary>
public sealed class EngineProgress
{
    public long TotalTicks { get; init; }

    public long Step { get; init; }

    public int Errors { get; init; }

    public string? Phase { get; init; }

    public IReadOnlyList<NodeStatusSnapshot>? Nodes { get; init; }
    public IReadOnlyList<NetworkLinkSnapshot>? Links { get; init; }
}
