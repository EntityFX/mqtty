using System.Diagnostics;
using System.Globalization;
using EntityFX.MqttY.Federation;

namespace EntityFX.MqttY.MqttRelay.App;

/// <summary>
/// Sequential vs parallel relay benchmark on the production FederationRunner
/// path (Relay mode). The legacy MqttRelayApp sweep publishes in bursts and
/// then drains for thousands of empty ticks, so it under-reports the parallel
/// refresh speedup. FederationRunner drives a continuous credit load, which is
/// the same workload the reference Benchmark.Compare measures at ~7.6x.
///
/// CPU and memory are sampled from a background thread every 200 ms via
/// Process.TotalProcessorTime / WorkingSet64 (cross-platform, no P/Invoke).
/// The sampler costs five property reads per second, which is far below the
/// measurement noise of a multi-second run.
/// </summary>
public static class RelayBenchmarkCommand
{
    public static int Run(string[] args)
    {
        try
        {
            var brokers = Math.Clamp(ValueInt(args, "brokers") ?? 16, 1, 16);
            var publishers = ValueInt(args, "publishers") ?? 64;
            var rps = ValueDouble(args, "rps") ?? 2000;
            var measure = ValueDouble(args, "measure") ?? 3;
            var warmup = ValueDouble(args, "warmup") ?? 0.01;
            var drain = ValueDouble(args, "drain") ?? 10;
            var repeats = Math.Max(1, ValueInt(args, "repeat") ?? 5);
            var seed = ValueInt(args, "seed") ?? 42;
            var qos = ValueInt(args, "qos") ?? 1;
            var workers = ValueInt(args, "workers") ?? EntityFX.MqttY.Network.CpuTopology.PhysicalCoreCount();

            // The refresh pool reads MQTTY_WORKERS at simulation construction.
            Environment.SetEnvironmentVariable("MQTTY_WORKERS",
                workers.ToString(CultureInfo.InvariantCulture));
            Environment.SetEnvironmentVariable("MQTTY_NODE_CHUNK", "1");
            if (Environment.GetEnvironmentVariable("MQTTY_BARRIER") == null)
            {
                Environment.SetEnvironmentVariable("MQTTY_BARRIER", "spin");
            }

            var options = new FederationOptions
            {
                Mode = FederationMode.Relay,
                Brokers = brokers,
                Topology = "star",
                Publishers = Math.Max(publishers, brokers),
                PayloadBytes = 16,
                Qos = qos,
                OfferedRps = rps,
                Seed = seed,
                WarmupSeconds = warmup,
                MeasurementSeconds = measure,
                DrainTimeoutSeconds = drain,
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
            options.Validate();
            var input = FederationInput.Prepare(options);

            var logical = Environment.ProcessorCount;
            var physical = EntityFX.MqttY.Network.CpuTopology.PhysicalCoreCount();
            Console.WriteLine("MqttY relay benchmark (FederationRunner, Relay mode)");
            Console.WriteLine($"CPU: {logical} logical / {physical} physical, workers={workers}");
            Console.WriteLine($"Repetitions: {repeats} (+1 warm-up), measure={measure:F2}s, " +
                              $"warmup={warmup:F2}s, drain={drain:F2}s");
            Console.WriteLine($"Topology: brokers={brokers}, publishers={options.Publishers}, " +
                              $"rps={rps}, seed={seed}");
            Console.WriteLine("Export: disabled");
            Console.WriteLine(new string('=', 78));
            Console.WriteLine("--- Relay ---");

            // Warm-up: JIT the tick path and size the worker pool for both modes.
            RunOnce(input, parallel: false);
            RunOnce(input, parallel: true);

            var seq = new List<RunSample>(repeats);
            var par = new List<RunSample>(repeats);
            for (var i = 0; i < repeats; i++)
            {
                // Alternate seq/par so thermal drift affects both equally.
                seq.Add(RunOnce(input, parallel: false));
                par.Add(RunOnce(input, parallel: true));
            }

            var engine = new EngineRow("Relay", seq, par, repeats, workers, logical);
            PrintEngine(engine);
            PrintSummary(engine);
            return engine.CoreMatches && engine.Seq.Status == "completed" && engine.Par.Status == "completed"
                ? 0
                : 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private sealed record RunSample(
        double Wall,
        double CpuSeconds,
        double AverageMemoryMb,
        double PeakMemoryMb,
        double Refresh,
        FederationSummary Summary);

    private sealed class EngineRow
    {
        public EngineRow(string name, List<RunSample> seq, List<RunSample> par, int repeats,
            int workers, int logicalCpus)
        {
            Name = name;
            Seq = seq[seq.Count / 2].Summary;
            Par = par[par.Count / 2].Summary;
            SeqWall = Median(seq.Select(s => s.Wall));
            ParWall = Median(par.Select(s => s.Wall));
            SeqRefresh = Median(seq.Select(s => s.Refresh));
            ParRefresh = Median(par.Select(s => s.Refresh));
            SeqCv = Cv(seq.Select(s => s.Wall));
            ParCv = Cv(par.Select(s => s.Wall));
            WallSpeedup = ParWall > 0 ? SeqWall / ParWall : 0;
            RefreshSpeedup = ParRefresh > 0 ? SeqRefresh / ParRefresh : 0;
            SeqCpu = Median(seq.Select(s => s.CpuSeconds));
            ParCpu = Median(par.Select(s => s.CpuSeconds));
            // Mean utilization over the run, normalised by logical CPUs so a
            // 12-core box fully busy on 12 workers reads ~50% of 24 logical.
            SeqCpuUtilization = SeqWall > 0 ? SeqCpu / SeqWall / logicalCpus * 100.0 : 0;
            ParCpuUtilization = ParWall > 0 ? ParCpu / ParWall / logicalCpus * 100.0 : 0;
            SeqMemory = Median(seq.Select(s => s.AverageMemoryMb));
            ParMemory = Median(par.Select(s => s.AverageMemoryMb));
            SeqPeakMemory = seq.Max(s => s.PeakMemoryMb);
            ParPeakMemory = par.Max(s => s.PeakMemoryMb);
            Workers = workers;
            CoreMatches = CoreMetricsMatch(Seq, Par);
        }

        public string Name { get; }
        public FederationSummary Seq { get; }
        public FederationSummary Par { get; }
        public double SeqWall { get; }
        public double ParWall { get; }
        public double SeqRefresh { get; }
        public double ParRefresh { get; }
        public double SeqCv { get; }
        public double ParCv { get; }
        public double WallSpeedup { get; }
        public double RefreshSpeedup { get; }
        public double SeqCpu { get; }
        public double ParCpu { get; }
        public double SeqCpuUtilization { get; }
        public double ParCpuUtilization { get; }
        public double SeqMemory { get; }
        public double ParMemory { get; }
        public double SeqPeakMemory { get; }
        public double ParPeakMemory { get; }
        public int Workers { get; }
        public bool CoreMatches { get; }
    }

    private static void PrintEngine(EngineRow row)
    {
        var errors = row.Seq.SendRejected + row.Seq.QueueDrops +
                     row.Par.SendRejected + row.Par.QueueDrops;
        Console.WriteLine($"  seq wall  median={row.SeqWall,8:F4}s  cv={row.SeqCv:F1}%");
        Console.WriteLine($"  par wall  median={row.ParWall,8:F4}s  cv={row.ParCv:F1}%");
        Console.WriteLine($"  refresh   seq={row.SeqRefresh,8:F4}s  par={row.ParRefresh,8:F4}s  " +
                          $"speedup={row.RefreshSpeedup:F2}x");
        Console.WriteLine($"  cpu       seq={row.SeqCpu,7:F3}s  par={row.ParCpu,7:F3}s  " +
                          $"mean utilization: seq={row.SeqCpuUtilization:F1}%  " +
                          $"par={row.ParCpuUtilization:F1}% of {Environment.ProcessorCount} logical");
        Console.WriteLine($"  memory    mean working set: seq={row.SeqMemory:F0} MB  par={row.ParMemory:F0} MB  " +
                          $"peak: seq={row.SeqPeakMemory:F0} MB  par={row.ParPeakMemory:F0} MB");
        Console.WriteLine($"  speedup   {row.WallSpeedup:F2}x   status={row.Seq.Status}/{row.Par.Status}   " +
                          $"errors={errors}   core={(row.CoreMatches ? "match" : "MISMATCH")}");
        Console.WriteLine();
    }

    private static void PrintSummary(EngineRow row)
    {
        var cpuText = string.Format(CultureInfo.InvariantCulture, "{0:F0}/{1:F0}%",
            row.SeqCpuUtilization, row.ParCpuUtilization);
        var memoryText = string.Format(CultureInfo.InvariantCulture, "{0:F0}/{1:F0}",
            row.SeqMemory, row.ParMemory);
        var statusText = row.Seq.Status + "/" + row.Par.Status;
        var coreText = row.CoreMatches ? "match" : "MISMATCH";

        Console.WriteLine(new string('=', 78));
        Console.WriteLine("SUMMARY (wall-clock, median)");
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(
            "Engine      seq        par    wall x   refresh x   cpu seq/par   mem seq/par MB   status        core");
        Console.WriteLine(new string('-', 110));
        Console.WriteLine(
            $"{row.Name,-10} {row.SeqWall,8:F3}s {row.ParWall,8:F3}s {row.WallSpeedup,8:F2}x " +
            $"{row.RefreshSpeedup,10:F2}x {cpuText,13} {memoryText,16}   {statusText,-22} {coreText}");
        Console.WriteLine();
        Console.WriteLine($"Wall speedup: {row.WallSpeedup:F2}x, refresh speedup: {row.RefreshSpeedup:F2}x " +
                          $"(workers={row.Workers})");
    }

    private static RunSample RunOnce(FederationInput input, bool parallel)
    {
        var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();
        using var sampler = new ResourceSampler(process);
        var runner = new FederationRunner();
        var summary = runner.Run(input, output: null, diagnostics: false, parallel: parallel,
            refreshStrategy: 1);
        stopwatch.Stop();
        var cpuAfter = process.TotalProcessorTime;
        sampler.Stop();
        return new RunSample(stopwatch.Elapsed.TotalSeconds, (cpuAfter - cpuBefore).TotalSeconds,
            sampler.AverageWorkingSetMb, sampler.PeakWorkingSetMb, summary.RefreshWallSeconds, summary);
    }

    /// <summary>
    /// Lightweight background sampler: one WorkingSet64 read every 200 ms.
    /// Costs five reads per second, so it does not perturb the measured wall.
    /// </summary>
    private sealed class ResourceSampler : IDisposable
    {
        private readonly Process _process;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _stop = new(false);
        private long _sumWorkingSet;
        private long _samples;
        private long _peakWorkingSet;

        public ResourceSampler(Process process)
        {
            _process = process;
            _thread = new Thread(Loop) { IsBackground = true, Name = "mqtty-bench-sampler" };
            _thread.Start();
        }

        public double AverageWorkingSetMb => _samples == 0
            ? 0
            : _sumWorkingSet / (double)_samples / (1024.0 * 1024.0);

        public double PeakWorkingSetMb => _peakWorkingSet / (1024.0 * 1024.0);

        private void Loop()
        {
            while (!_stop.Wait(200))
            {
                try
                {
                    var workingSet = _process.WorkingSet64;
                    Interlocked.Add(ref _sumWorkingSet, workingSet);
                    Interlocked.Increment(ref _samples);
                    var previousPeak = Interlocked.Read(ref _peakWorkingSet);
                    if (workingSet > previousPeak)
                    {
                        Interlocked.CompareExchange(ref _peakWorkingSet, workingSet, previousPeak);
                    }
                }
                catch
                {
                    // Process exited or the property is unavailable; stop quietly.
                }
            }
        }

        public void Stop() => _stop.Set();

        public void Dispose()
        {
            _stop.Set();
            _thread.Join(TimeSpan.FromSeconds(1));
            _stop.Dispose();
        }
    }

    private static bool CoreMetricsMatch(FederationSummary seq, FederationSummary par) =>
        seq.ScheduledPublishes == par.ScheduledPublishes &&
        seq.ApiAttempts == par.ApiAttempts &&
        seq.SourceCompleted == par.SourceCompleted &&
        seq.UniqueDeliveries == par.UniqueDeliveries &&
        seq.ForwardAccepted == par.ForwardAccepted &&
        seq.ForwardRejected == par.ForwardRejected &&
        seq.QueueDrops == par.QueueDrops &&
        seq.PublishP50Ms == par.PublishP50Ms &&
        seq.PublishP99Ms == par.PublishP99Ms;

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static double Cv(IEnumerable<double> values)
    {
        var array = values.ToArray();
        if (array.Length < 2)
        {
            return 0;
        }

        var mean = array.Average();
        if (mean == 0)
        {
            return 0;
        }

        var variance = array.Sum(value => (value - mean) * (value - mean)) / (array.Length - 1);
        return Math.Sqrt(variance) / mean * 100.0;
    }

    private static int? ValueInt(string[] args, string name) =>
        int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? ValueDouble(string[] args, string name) =>
        double.TryParse(Value(args, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string? Value(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--" + name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
