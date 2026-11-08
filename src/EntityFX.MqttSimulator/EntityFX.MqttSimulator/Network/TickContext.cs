using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Immutable per-tick execution context passed through the tick pipeline.
/// </summary>
public readonly struct TickContext
{
    public TickContext(long tick, long steps, bool parallel, int strategy, NetworkLoggerScope? scope)
    {
        Tick = tick;
        Steps = steps;
        Parallel = parallel;
        Strategy = strategy;
        Scope = scope;
    }

    public long Tick { get; }

    /// <summary>Step total captured at the start of the tick.</summary>
    public long Steps { get; }

    public bool Parallel { get; }

    public int Strategy { get; }

    public NetworkLoggerScope? Scope { get; }
}
