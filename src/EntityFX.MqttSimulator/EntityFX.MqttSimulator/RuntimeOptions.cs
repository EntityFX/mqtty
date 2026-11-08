namespace EntityFX.MqttY;

public enum TickBarrierMode
{
    Auto,
    Spin,
    Monitor
}

/// <summary>
/// Environment-driven runtime switches. A snapshot is read when a simulation is
/// created, so tests and the benchmark can change MQTTY_* variables between runs
/// without the process-wide static caching that used to pin the first value.
/// </summary>
public sealed record RuntimeOptions
{
    public int? Workers { get; init; }
    public TickBarrierMode Barrier { get; init; } = TickBarrierMode.Auto;
    public int SpinIterations { get; init; } = 64;
    public int BarrierSpinIterations { get; init; } = 64;
    public int? NodeChunk { get; init; }
    public int? NetworkChunk { get; init; }
    public bool ParallelPublish { get; init; }
    public bool IsolateNetworks { get; init; }
    public bool DisableCounters { get; init; }
    public bool ActiveOnly { get; init; }
    public bool RefreshStats { get; init; }
    public bool WorkerStats { get; init; }
    public bool NodeStats { get; init; }
    public bool NonAtomicPacketId { get; init; }

    public int ResolveWorkers() => Workers ?? Network.CpuTopology.PhysicalCoreCount();

    public static RuntimeOptions FromEnvironment() => new()
    {
        Workers = Positive("MQTTY_WORKERS"),
        Barrier = BarrierMode("MQTTY_BARRIER"),
        SpinIterations = NonNegative("MQTTY_SPIN_ITERATIONS", 64),
        BarrierSpinIterations = NonNegative("MQTTY_BARRIER_SPIN", 64),
        NodeChunk = Positive("MQTTY_NODE_CHUNK"),
        NetworkChunk = Positive("MQTTY_NETWORK_CHUNK"),
        ParallelPublish = Flag("MQTTY_PARALLEL_PUBLISH"),
        IsolateNetworks = Flag("MQTTY_ISOLATE_NETWORKS"),
        DisableCounters = Flag("MQTTY_DISABLE_COUNTERS"),
        ActiveOnly = Flag("MQTTY_ACTIVE_ONLY"),
        RefreshStats = Flag("MQTTY_REFRESH_STATS"),
        WorkerStats = Flag("MQTTY_WORKER_STATS"),
        NodeStats = Flag("MQTTY_NODE_STATS"),
        NonAtomicPacketId = Flag("MQTTY_NONATOMIC_ID")
    };

    private static bool Flag(string name) => Environment.GetEnvironmentVariable(name) == "1";

    private static int? Positive(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : null;

    private static int NonNegative(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value >= 0 ? value : fallback;

    private static TickBarrierMode BarrierMode(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.Equals(value, "spin", StringComparison.OrdinalIgnoreCase)) return TickBarrierMode.Spin;
        if (string.Equals(value, "monitor", StringComparison.OrdinalIgnoreCase)) return TickBarrierMode.Monitor;
        return TickBarrierMode.Auto;
    }
}
