using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Network;

// =====================================================================
// Reference benchmark: sequential (parallel=false) vs parallel (parallel=true)
// for LoadBalancing, Relay and Federation.
//
// The protocol is deliberately strict:
//   * a discarded warm-up pair;
//   * alternating seq/par order between repetitions;
//   * median (and min/max/CV) instead of a single run;
//   * export can be excluded from the gate;
//   * seq and par scientific metrics must match.
//
// Example gate run:
//   dotnet run -c Release --no-build -- --repeat 5 --measure 5 --drain 10 --no-export
// =====================================================================

var opt = BenchOptions.Parse(args);
var (logicalCpus, physicalCpus) = Hardware.DetectCores();
var workers = opt.Workers > 0 ? opt.Workers : Math.Min(logicalCpus, physicalCpus);
// MQTTY_WORKERS is set per sample in RunEngine and restored afterwards, so an
// embedded/test host is not left with a mutated process environment.
var tmp = Path.Combine(Path.GetTempPath(), $"mqtty-bench-{Guid.NewGuid():N}");
var exportSerial = new int[1];
if (!opt.NoExport) Directory.CreateDirectory(tmp);

Console.WriteLine($"MqttY reference benchmark");
Console.WriteLine($"CPU: {logicalCpus} logical / {physicalCpus} physical, workers={workers}");
Console.WriteLine($"Repetitions: {opt.Repeat} (+{opt.WarmupRuns} warm-up), measure={opt.Measure:F2}s, warmup={opt.Warmup:F2}s, drain={opt.Drain:F2}s");
Console.WriteLine($"Topology: brokers={opt.Brokers}, nets/node={opt.Nets}, publishers={opt.Publishers}, seed={opt.Seed}");
Console.WriteLine($"Export: {(opt.NoExport ? "disabled" : tmp)}");
if (opt.Measure < 1 || opt.WarmupRuns == 0)
{
    Console.WriteLine("WARNING: smoke configuration (measure < 1 s or no warm-up); not a gate run.");
}
Console.WriteLine(new string('=', 78));

var engines = new List<EngineReport>();

if (opt.Engines.Contains("lb"))
{
    var lbOptions = BuildLbOptions(opt);
    var lbInput = ExperimentInput.Prepare(lbOptions,
        new InputSnapshot("generated-from-benchmark", JsonSerializer.SerializeToUtf8Bytes(lbOptions, ExperimentInput.Json)));
    var lbRunner = new LoadBalancingExperimentRunner();
    engines.Add(RunEngine("LoadBalancing", opt, (parallel, rep) =>
    {
        var sw = Stopwatch.StartNew();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var (summary, timing) = lbRunner.RunMeasured(lbInput, ExportDir("lb", parallel, rep), parallel, refreshStrategy: 0);
        sw.Stop();
        if (parallel && Environment.GetEnvironmentVariable("MQTTY_PHASE_STATS") == "1")
        {
            Console.Error.WriteLine("phases-wall " + string.Join(" ", timing.PhaseWallSeconds.Select(p => $"{p.Key}={p.Value:F4}")));
            Console.Error.WriteLine("phases-refresh " + string.Join(" ", timing.RefreshWallSecondsByPhase.Select(p => $"{p.Key}={p.Value:F4}")));
            Console.Error.WriteLine("phases-calls " + string.Join(" ", timing.RefreshCallsByPhase.Select(p => $"{p.Key}={p.Value}")));
        }
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
        return new RunSample(parallel ? "par" : "seq", rep, sw.Elapsed.TotalSeconds, timing.RefreshWallSeconds,
            timing.ExportWallSeconds, summary.GraphErrors, summary.Status, Metrics.Lb(summary), cpu,
            Attempts: summary.ApiAttempts, Unique: summary.Delivery.UniqueDelivered,
            Steps: summary.ScheduledPublishes,
            NetworkInbound: timing.NetworkInboundPackets,
            NetworkOutbound: timing.NetworkOutboundPackets);
    }));
}

if (opt.Engines.Contains("relay"))
{
    // Product relay engine: FederationRunner in Relay mode, not the legacy
    // MqttRelayApp benchmark path.
    var relayOptions = BuildRelayOptions(opt);
    var relayInput = FederationInput.Prepare(relayOptions);
    var relayRunner = new FederationRunner();
    engines.Add(RunEngine("Relay", opt, (parallel, rep) =>
    {
        var sw = Stopwatch.StartNew();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var relayPhaseWalls = new Dictionary<string, double>();
        long relayNetIn = 0, relayNetOut = 0;
        var summary = relayRunner.Run(relayInput, ExportDir("relay", parallel, rep), diagnostics: false,
            parallel: parallel, refreshStrategy: 1, phaseWallSeconds: relayPhaseWalls,
            networkPackets: (inbound, outbound) => { relayNetIn = inbound; relayNetOut = outbound; });
        sw.Stop();
        if (parallel && Environment.GetEnvironmentVariable("MQTTY_PHASE_STATS") == "1")
        {
            Console.Error.WriteLine("fed-phases-wall " + string.Join(" ", relayPhaseWalls.Select(p => $"{p.Key}={p.Value:F4}")));
        }
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
        return new RunSample(parallel ? "par" : "seq", rep, sw.Elapsed.TotalSeconds,
            summary.RefreshWallSeconds, 0, summary.ForwardRejected + summary.QueueDrops,
            summary.Status, Metrics.Fed(summary), cpu,
            Attempts: summary.ApiAttempts, Unique: summary.UniqueDeliveries,
            Steps: summary.ScheduledPublishes,
            NetworkInbound: relayNetIn, NetworkOutbound: relayNetOut);
    }));
}

if (opt.Engines.Contains("fed"))
{
    var fedOptions = BuildFedOptions(opt);
    var fedInput = FederationInput.Prepare(fedOptions);
    var fedRunner = new FederationRunner();
    engines.Add(RunEngine("Federation", opt, (parallel, rep) =>
    {
        var sw = Stopwatch.StartNew();
        var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var fedPhaseWalls = new Dictionary<string, double>();
        long fedNetIn = 0, fedNetOut = 0;
        var summary = fedRunner.Run(fedInput, ExportDir("fed", parallel, rep), diagnostics: false,
            parallel: parallel, refreshStrategy: 1, phaseWallSeconds: fedPhaseWalls,
            networkPackets: (inbound, outbound) => { fedNetIn = inbound; fedNetOut = outbound; });
        sw.Stop();
        if (parallel && Environment.GetEnvironmentVariable("MQTTY_PHASE_STATS") == "1")
        {
            Console.Error.WriteLine("fed-phases-wall " + string.Join(" ", fedPhaseWalls.Select(p => $"{p.Key}={p.Value:F4}")));
        }
        var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
        return new RunSample(parallel ? "par" : "seq", rep, sw.Elapsed.TotalSeconds, summary.RefreshWallSeconds, 0,
            summary.ForwardRejected + summary.QueueDrops, summary.Status, Metrics.Fed(summary), cpu,
            Attempts: summary.ApiAttempts, Unique: summary.UniqueDeliveries,
            Steps: summary.ScheduledPublishes,
            NetworkInbound: fedNetIn, NetworkOutbound: fedNetOut);
    }));
}

PrintSummary(engines);
Console.WriteLine($"GC: allocated={GC.GetTotalAllocatedBytes(precise: false) / 1_000_000.0:F0} MB, " +
                  $"gen0={GC.CollectionCount(0)}, gen1={GC.CollectionCount(1)}, gen2={GC.CollectionCount(2)}");
Console.WriteLine();

if (opt.Strict && engines.Any(e => !e.MetricsMatch || e.Status != "completed" || !e.Activity))
{
    Console.WriteLine("STRICT MODE: metric mismatch, invalid activity or non-completed run.");
    Environment.ExitCode = 2;
}

if (opt.Strict && (opt.GateMinWall > 0 || opt.GateMinRefresh > 0))
{
    var failures = engines.Where(engine =>
        engine.Status == "completed" &&
        ((opt.GateMinWall > 0 && engine.WallSpeedup < opt.GateMinWall) ||
         (opt.GateMinRefresh > 0 && engine.RefreshSpeedup < opt.GateMinRefresh))).ToArray();
    foreach (var engine in failures)
    {
        Console.WriteLine($"GATE FAIL: {engine.Engine} wall={engine.WallSpeedup:F2}x " +
                          $"refresh={engine.RefreshSpeedup:F2}x (min wall={opt.GateMinWall:F2}x, " +
                          $"min refresh={opt.GateMinRefresh:F2}x)");
    }
    if (failures.Length > 0)
    {
        Environment.ExitCode = 3;
    }
}

if (opt.JsonPath != null)
{
    var report = new BenchReport(DateTimeOffset.UtcNow, logicalCpus, physicalCpus, opt, engines);
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(opt.JsonPath, json);
    Console.WriteLine($"JSON: {opt.JsonPath}");
}

return Environment.ExitCode;

string? ExportDir(string engine, bool parallel, int repetition) =>
    opt.NoExport ? null : Path.Combine(tmp, $"{engine}-{(parallel ? "par" : "seq")}-{repetition}-{++exportSerial[0]}");

EngineReport RunEngine(string name, BenchOptions options, Func<bool, int, RunSample> run)
{
    Console.WriteLine($"--- {name} ---");
    var modes = options.Mode switch
    {
        "seq" => new[] { "seq" },
        "par" => new[] { "par" },
        _ => new[] { "seq", "par" }
    };

    RunSample SafeRun(bool parallel, int rep)
    {
        var previousWorkers = Environment.GetEnvironmentVariable("MQTTY_WORKERS");
        Environment.SetEnvironmentVariable("MQTTY_WORKERS", workers.ToString(CultureInfo.InvariantCulture));
        // Samples share a process. Reclaim the previous topology before the
        // next timed run so its collection is not charged to construct/connect
        // (a standalone run never inherits another simulation's heap).
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        try
        {
            return run(parallel, rep);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"  {name} {(parallel ? "par" : "seq")} rep={rep} failed: {exception.Message}");
            return new RunSample(parallel ? "par" : "seq", rep, 0, 0, 0, 0, "error",
                new MetricSet("{}", "{}"), 0, Failure: exception.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MQTTY_WORKERS", previousWorkers);
        }
    }

    for (var warm = 0; warm < options.WarmupRuns; warm++)
    {
        foreach (var mode in modes)
        {
            _ = SafeRun(mode == "par", 0);
        }
    }

    var samples = new List<RunSample>(options.Repeat * modes.Length);
    for (var rep = 0; rep < options.Repeat; rep++)
    {
        if (modes.Length == 1)
        {
            samples.Add(SafeRun(modes[0] == "par", rep));
            continue;
        }

        // Alternate the order: the second run is warmer, so always running seq
        // first systematically biases the speedup in favour of par.
        if (rep % 2 == 0)
        {
            samples.Add(SafeRun(false, rep));
            samples.Add(SafeRun(true, rep));
        }
        else
        {
            samples.Add(SafeRun(true, rep));
            samples.Add(SafeRun(false, rep));
        }
    }

    var seq = samples.Where(s => s.Mode == "seq").ToArray();
    var par = samples.Where(s => s.Mode == "par").ToArray();
    var seqWall = seq.Length > 0 ? median(seq.Select(s => s.Wall)) : 0;
    var parWall = par.Length > 0 ? median(par.Select(s => s.Wall)) : 0;
    var seqRefresh = seq.Length > 0 ? median(seq.Select(s => s.Refresh)) : 0;
    var parRefresh = par.Length > 0 ? median(par.Select(s => s.Refresh)) : 0;
    var seqExport = seq.Length > 0 ? median(seq.Select(s => s.Export)) : 0;
    var parExport = par.Length > 0 ? median(par.Select(s => s.Export)) : 0;
    var seqCpu = seq.Length > 0 ? median(seq.Select(s => s.CpuSeconds)) : 0;
    var parCpu = par.Length > 0 ? median(par.Select(s => s.CpuSeconds)) : 0;
    var wallSpeedup = seqWall > 0 && parWall > 0 ? seqWall / parWall : 0;
    var refreshSpeedup = seqRefresh > 0 && parRefresh > 0 ? seqRefresh / parRefresh : 0;
    var metricReference = samples[0].Metrics.Core;
    var metricsMatch = samples.All(s => s.Metrics.Core == metricReference);
    var diagnosticsMatch = samples.All(s => s.Metrics.Diagnostics == samples[0].Metrics.Diagnostics);
    var status = samples.All(s => s.Status == "completed") ? "completed" : "failed";
    var activity = samples.All(s => s.Steps > 0 && s.Attempts > 0 && s.Unique > 0);
    if (!activity) status = "invalid";
    var errors = par.Sum(s => s.Errors) + seq.Sum(s => s.Errors);

    Console.WriteLine($"  seq wall  median={(seq.Length > 0 ? seqWall.ToString("F4") : "n/a"),8}s  min={(seq.Length > 0 ? seq.Min(s => s.Wall).ToString("F4") : "n/a"),8}  max={(seq.Length > 0 ? seq.Max(s => s.Wall).ToString("F4") : "n/a"),8}  cv={(seq.Length > 0 ? cv(seq.Select(s => s.Wall)) : 0):F1}%");
    Console.WriteLine($"  par wall  median={(par.Length > 0 ? parWall.ToString("F4") : "n/a"),8}s  min={(par.Length > 0 ? par.Min(s => s.Wall).ToString("F4") : "n/a"),8}  max={(par.Length > 0 ? par.Max(s => s.Wall).ToString("F4") : "n/a"),8}  cv={(par.Length > 0 ? cv(par.Select(s => s.Wall)) : 0):F1}%");
    Console.WriteLine($"  refresh   seq={seqRefresh:F4}s  par={parRefresh:F4}s  speedup={(refreshSpeedup > 0 ? refreshSpeedup.ToString("F2") + "x" : "n/a")}");
    Console.WriteLine($"  export    seq={seqExport:F4}s  par={parExport:F4}s");
    Console.WriteLine($"  cpu       seq={seqCpu:F3}s  par={parCpu:F3}s  ({(seqCpu > 0 ? parCpu / seqCpu : 0):F2}x)");
    Console.WriteLine($"  speedup   {(wallSpeedup > 0 ? wallSpeedup.ToString("F2") + "x" : "n/a")}   status={status}   errors={errors}   activity={(activity ? "ok" : "EMPTY")}   core={(metricsMatch ? "match" : "MISMATCH")}   diagnostics={(diagnosticsMatch ? "match" : "differ")}");
    Console.WriteLine();

    return new EngineReport(name, seqWall, parWall, wallSpeedup, seqRefresh, parRefresh, refreshSpeedup,
        seqExport, parExport, seqCpu, parCpu, status, errors, metricsMatch, diagnosticsMatch, activity, samples);

    static double median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(v => v).ToArray();
        return ordered.Length == 0 ? 0 : ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
    }

    static double cv(IEnumerable<double> values)
    {
        var array = values.ToArray();
        if (array.Length < 2) return 0;
        var mean = array.Average();
        return mean == 0 ? 0 : Math.Sqrt(array.Sum(v => (v - mean) * (v - mean)) / (array.Length - 1)) / mean * 100;
    }
}

void PrintSummary(List<EngineReport> reports)
{
    Console.WriteLine(new string('=', 78));
    Console.WriteLine("SUMMARY (wall-clock, median)");
    Console.WriteLine(new string('=', 78));
    Console.WriteLine($"{"Engine",-16} {"seq",10} {"par",10} {"wall x",8} {"refresh x",10} {"status",-12} {"core",-8} {"diag",-8}");
    Console.WriteLine(new string('-', 78));
    foreach (var e in reports)
    {
        Console.WriteLine($"{e.Engine,-16} {e.SeqWall,9:F3}s {e.ParWall,9:F3}s {e.WallSpeedup,7:F2}x " +
                          $"{(e.RefreshSpeedup > 0 ? e.RefreshSpeedup.ToString("F2") + "x" : "n/a"),9} {e.Status,-12} {(e.MetricsMatch ? "match" : "MISMATCH"),-8} {(e.DiagnosticsMatch ? "match" : "differ"),-8}");
    }
    Console.WriteLine();
    Console.WriteLine("Gate reminder: refresh >= 4x on 8 physical cores; 3.5-4.5x on 4C/8T.");
}

LoadBalancingOptions BuildLbOptions(BenchOptions o)
{
    var result = new LoadBalancingOptions
    {
        Ideal = true,
        Mode = BalancingMode.RoundRobin,
        BrokerType = "Aedes",
        BackendCount = o.Brokers,
        TotalPublishers = o.Publishers,
        PayloadBytes = 16,
        PublishQos = o.LbQos,
        SubscribeQos = o.LbQos,
        OfferedRps = o.LbRps,
        Seed = o.Seed,
        WarmupSeconds = o.Warmup,
        MeasurementSeconds = o.Measure,
        DrainTimeoutSeconds = o.Drain,
        TickPeriod = TimeSpan.FromTicks(1000),
        NetworkLength = o.Nets,
        Network = new NetworkSettings
        {
            CapacityBytesPerSecond = 100_000_000,
            ThroughputWindowTicks = 100,
            QueueCapacity = 100_000,
            TransferTicks = 1,
        }
    };
    result.Validate();
    return result;
}

FederationOptions BuildRelayOptions(BenchOptions o)
{
    var result = new FederationOptions
    {
        Mode = FederationMode.Relay,
        Brokers = Math.Min(o.Brokers, 16),
        Topology = "star",
        Publishers = Math.Max(o.Publishers, Math.Min(o.Brokers, 16)),
        PayloadBytes = 16,
        Qos = 1,
        OfferedRps = o.FedRps,
        Seed = o.Seed,
        WarmupSeconds = o.Warmup,
        MeasurementSeconds = o.Measure,
        DrainTimeoutSeconds = o.Drain,
        TickPeriod = TimeSpan.FromTicks(1000),
        Ideal = true,
        Network = new FederationNetworkOptions
        {
            CapacityBytesPerSecond = 100_000_000,
            ThroughputWindowTicks = 100,
            QueueCapacity = 100_000,
            TransferTicks = 1,
        }
    };
    result.Validate();
    return result;
}

FederationOptions BuildFedOptions(BenchOptions o)
{
    var result = new FederationOptions
    {
        Mode = FederationMode.Federation,
        Brokers = Math.Min(o.Brokers, 16),
        Topology = o.FedTopology,
        Publishers = Math.Max(o.Publishers, Math.Min(o.Brokers, 16)),
        PayloadBytes = 16,
        Qos = 1,
        OfferedRps = o.FedRps,
        Seed = o.Seed,
        WarmupSeconds = o.Warmup,
        MeasurementSeconds = o.Measure,
        DrainTimeoutSeconds = o.Drain,
        TickPeriod = TimeSpan.FromTicks(1000),
        Ideal = true,
        Network = new FederationNetworkOptions
        {
            CapacityBytesPerSecond = 100_000_000,
            ThroughputWindowTicks = 100,
            QueueCapacity = 100_000,
            TransferTicks = 1,
        }
    };
    result.Validate();
    return result;
}

internal sealed record MetricSet(string Core, string Diagnostics);

internal sealed record RunSample(string Mode, int Repetition, double Wall, double Refresh, double Export,
    long Errors, string Status, MetricSet Metrics, double CpuSeconds,
    long Attempts = 0, long Unique = 0, long Steps = 0, string? Failure = null,
    long NetworkInbound = 0, long NetworkOutbound = 0);

internal sealed record EngineReport(string Engine, double SeqWall, double ParWall, double WallSpeedup,
    double SeqRefresh, double ParRefresh, double RefreshSpeedup, double SeqExport, double ParExport,
    double SeqCpu, double ParCpu, string Status, long Errors, bool MetricsMatch, bool DiagnosticsMatch,
    bool Activity, IReadOnlyList<RunSample> Samples);

internal sealed record BenchReport(DateTimeOffset CreatedUtc, int LogicalCpus, int PhysicalCpus,
    BenchOptions Options, IReadOnlyList<EngineReport> Engines);

internal sealed class BenchOptions
{
    public HashSet<string> Engines { get; } = new(StringComparer.OrdinalIgnoreCase) { "lb", "relay", "fed" };
    public int Repeat { get; private set; } = 3;
    public int WarmupRuns { get; private set; } = 1;
    public int Workers { get; private set; }
    public double Measure { get; private set; } = 0.5;
    public double Warmup { get; private set; } = 0.01;
    public double Drain { get; private set; } = 1;
    public double LbRps { get; private set; } = 10_000;
    public int LbQos { get; private set; } = 1;
    public double FedRps { get; private set; } = 5_000;
    public int Brokers { get; private set; } = 16;
    public int Nets { get; private set; } = 4;
    public int Publishers { get; private set; } = 64;
    public int RelayClients { get; private set; } = 4;
    public int RelayRepeats { get; private set; } = 80;
    public int Seed { get; private set; } = 42;
    public string FedTopology { get; private set; } = "star";
    public string Mode { get; private set; } = "both";
    public bool NoExport { get; private set; }
    public bool Strict { get; private set; } = true;
    public double GateMinWall { get; private set; }
    public double GateMinRefresh { get; private set; }
    public string? JsonPath { get; private set; }

    public static BenchOptions Parse(string[] args)
    {
        var o = new BenchOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {arg}");
            switch (arg)
            {
                case "--engines":
                    o.Engines.Clear();
                    foreach (var engine in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        o.Engines.Add(engine);
                    break;
                case "--repeat": o.Repeat = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--warmup-runs": o.WarmupRuns = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--workers": o.Workers = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--measure": o.Measure = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--warmup": o.Warmup = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--drain": o.Drain = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--rps": o.LbRps = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--qos": o.LbQos = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--fed-rps": o.FedRps = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--brokers": o.Brokers = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--nets": o.Nets = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--publishers": o.Publishers = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--relay-clients": o.RelayClients = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--relay-repeats": o.RelayRepeats = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--seed": o.Seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--fed-topology": o.FedTopology = Next().ToLowerInvariant(); break;
                case "--mode": o.Mode = Next().ToLowerInvariant(); break;
                case "--no-export": o.NoExport = true; break;
                case "--no-strict": o.Strict = false; break;
                case "--gate-min-wall": o.GateMinWall = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--gate-min-refresh": o.GateMinRefresh = double.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--json": o.JsonPath = Next(); break;
                case "--help":
                    Console.WriteLine("Options: --engines lb,relay,fed --mode both|seq|par --repeat N --warmup-runs N --workers N --measure S --warmup S --drain S --rps N --fed-rps N --brokers N --nets N --publishers N --relay-clients N --relay-repeats N --seed N --qos N --fed-topology star|ring|line --no-export --no-strict --gate-min-wall X --gate-min-refresh X --json PATH");
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        if (o.Repeat < 1) throw new ArgumentOutOfRangeException(nameof(Repeat));
        if (o.Measure <= 0 || o.Warmup < 0 || o.Drain <= 0) throw new ArgumentOutOfRangeException(nameof(Measure));
        if (o.Brokers < 1 || o.Nets < 1 || o.Publishers < 1) throw new ArgumentOutOfRangeException(nameof(Brokers));
        if (o.Mode is not ("both" or "seq" or "par")) throw new ArgumentException("--mode must be both, seq or par");
        return o;
    }
}

internal static class Hardware
{
    public static (int Logical, int Physical) DetectCores()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/cpuinfo");
            var logical = lines.Count(l => l.StartsWith("processor", StringComparison.Ordinal));
            var coresPerSocket = lines.Where(l => l.StartsWith("cpu cores", StringComparison.Ordinal))
                .Select(l => int.Parse(l.Split(':')[1].Trim(), CultureInfo.InvariantCulture)).FirstOrDefault();
            var sockets = lines.Where(l => l.StartsWith("physical id", StringComparison.Ordinal))
                .Select(l => int.Parse(l.Split(':')[1].Trim(), CultureInfo.InvariantCulture)).Distinct().Count();
            if (logical > 0 && coresPerSocket > 0 && sockets > 0) return (logical, coresPerSocket * sockets);
            return (Environment.ProcessorCount, Environment.ProcessorCount);
        }
        catch
        {
            return (Environment.ProcessorCount, Environment.ProcessorCount);
        }
    }
}

internal static class Metrics
{
    public static MetricSet Lb(ExperimentSummary s)
    {
        var metrics = MetricsCollector.FromLoadBalancing(s);
        return new MetricSet(MetricsCollector.Serialize(metrics.Core),
            MetricsCollector.Serialize(metrics.Diagnostics));
    }

    public static MetricSet Fed(FederationSummary s)
    {
        var metrics = MetricsCollector.FromFederation(s);
        return new MetricSet(MetricsCollector.Serialize(metrics.Core),
            MetricsCollector.Serialize(metrics.Diagnostics));
    }
}
