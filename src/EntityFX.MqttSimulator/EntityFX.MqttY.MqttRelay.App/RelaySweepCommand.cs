using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace EntityFX.MqttY.MqttRelay.App;

/// <summary>
/// Which slice of the sweep this process owns. A single process owns
/// <c>Count == 1</c>, which is the historical behaviour.
/// </summary>
public readonly record struct ShardSpec(int Index, int Count, string? OutputPath, HashSet<int>? Ordinals = null)
{
    /// <summary>
    /// Ordinals are assigned by an LPT pass (heaviest configurations first) so
    /// shards finish at roughly the same time; the hash set carries that
    /// assignment. Without it the legacy round-robin split is used.
    /// </summary>
    public bool Contains(int ordinal) => Ordinals != null
        ? Ordinals.Contains(ordinal)
        : Count <= 1 || ordinal % Count == Index;
}

/// <summary>
/// Coarse-grained parallelism for the parameter sweep.
///
/// Every simulation in the sweep is independent, so the cheapest way to use all
/// cores without touching the bulk-synchronous tick model is to run whole
/// simulations in parallel processes. Each child owns a disjoint subset of the
/// configuration ordinals and executes the exact same code path, which keeps
/// results bit-identical to a single-process run (the only shared state,
/// <c>CounterClock</c>, is static per process, so process isolation is what
/// makes this safe).
///
/// Two useful modes:
///   * <c>--parallel N --workers 1</c>  — throughput across independent runs;
///     per-run timings are sequential, so use it to produce results, not to
///     compare seq vs par.
///   * <c>--parallel N --workers W</c> with <c>N * W ≈ physical cores</c> — keeps
///     the seq/par comparison meaningful while still filling the machine.
/// </summary>
public static class RelaySweepCommand
{
    private static readonly int[] DefaultBrokers = { 4, 5, 10, 15 };
    private static readonly int[] DefaultNets = { 2, 5, 10, 50 };
    private static readonly int[] DefaultClients = { 3, 10, 50 };
    private static readonly int[] DefaultRepeats = { 10, 100 };
    private static readonly int[] DefaultStrategies = { 0, 1 };

    /// <summary>True when the process was started as a shard child.</summary>
    public static bool HasShard(string[] args) => IndexOf(args, "shard") >= 0;

    public static ShardSpec ReadShard(string[] args)
    {
        var value = Value(args, "shard");
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ShardSpec(0, 1, Value(args, "output"));
        }

        var parts = value.Split('/');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count < 1 || index < 0 || index >= count)
        {
            throw new ArgumentException("--shard must be '<index>/<count>' with 0 <= index < count.");
        }

        return new ShardSpec(index, count, Value(args, "output"),
            ComputeShardAssignments(args, count, index));
    }

    /// <summary>
    /// Longest-processing-time first assignment: sort configurations by a
    /// relative cost estimate and give each to the currently lightest shard.
    /// A round-robin split can put several heavy configurations in one shard
    /// and leave the others idle at the end of the sweep.
    /// </summary>
    private static HashSet<int> ComputeShardAssignments(string[] args, int shards, int index)
    {
        var brokers = Brokers(args);
        var nets = Nets(args);
        var clients = Clients(args);
        var repeats = Repeats(args);
        var strategies = Strategies(args);
        var total = CheckedProduct(brokers.Length, nets.Length, clients.Length,
            repeats.Length, strategies.Length);

        var weights = new long[total];
        var ordinal = 0;
        foreach (var b in brokers)
        foreach (var n in nets)
        foreach (var c in clients)
        foreach (var r in repeats)
        foreach (var s in strategies)
        {
            weights[ordinal++] = Math.Max(1, (long)b) * Math.Max(1, n) * Math.Max(1, c) *
                                 (Math.Max(1, r) + 1) * (s == 0 ? 1 : 2);
        }

        var order = Enumerable.Range(0, total)
            .OrderByDescending(i => weights[i])
            .ThenBy(i => i)
            .ToArray();

        var loads = new long[shards];
        var assignments = new HashSet<int>[shards];
        for (var s = 0; s < shards; s++)
        {
            assignments[s] = new HashSet<int>();
        }

        foreach (var task in order)
        {
            var target = 0;
            for (var s = 1; s < shards; s++)
            {
                if (loads[s] < loads[target])
                {
                    target = s;
                }
            }

            assignments[target].Add(task);
            loads[target] += weights[task];
        }

        return assignments[Math.Clamp(index, 0, shards - 1)];
    }

    public static int[] Brokers(string[] args) => IntList(args, "brokers") ?? DefaultBrokers;
    public static int[] Nets(string[] args) => IntList(args, "nets") ?? DefaultNets;
    public static int[] Clients(string[] args) => IntList(args, "clients") ?? DefaultClients;
    public static int[] Repeats(string[] args) => IntList(args, "repeats") ?? DefaultRepeats;
    public static int[] Strategies(string[] args) => IntList(args, "strategies") ?? DefaultStrategies;

    /// <summary>Parent mode: split the sweep into shards, run them concurrently and merge.</summary>
    public static int Run(string[] args)
    {
        try
        {
            var total = CheckedProduct(
                Brokers(args).Length, Nets(args).Length, Clients(args).Length,
                Repeats(args).Length, Strategies(args).Length);
            var parallel = ValueInt(args, "parallel") ?? PhysicalCores();
            var shards = Math.Max(1, Math.Min(parallel, total));
            var workers = ValueInt(args, "workers") ?? Math.Max(1, PhysicalCores() / shards);
            var output = Value(args, "output")
                ?? Path.Combine("results", "sweep-" + DateTime.Now.ToString("yyyy_MM_dd__HH_mm_ss"));
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);

            Console.WriteLine($"Sweep: {total} configurations, {shards} concurrent shards, {workers} worker(s) per simulation");
            Console.WriteLine($"Output: {output}");

            // Cross-platform resource sampling: Process.TotalProcessorTime and
            // WorkingSet64 are available on Linux, Windows and macOS without
            // P/Invoke. CPU utilization is total process CPU time divided by
            // wall time and logical processor count.
            var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            var started = Stopwatch.StartNew();
            var children = new List<(int Index, Process Process, Task<string> Output, Task<string> Error)>();
            for (var index = 0; index < shards; index++)
            {
                var dir = Path.Combine(output, $"shard-{index}");
                Directory.CreateDirectory(dir);
                var child = StartShard(args, index, shards, workers, dir);
                // Drain both pipes as soon as each process starts. Waiting for
                // one child's stdout before reading the others can fill their
                // pipes and block the sweep, and buffering all output grows with
                // the size of the matrix.
                children.Add((index, child, child.StandardOutput.ReadToEndAsync(),
                    child.StandardError.ReadToEndAsync()));
            }

            var failed = false;
            var childrenCpu = TimeSpan.Zero;
            var childrenAllocatedMb = 0.0;
            long peakMemoryBytes = 0;
            for (var index = 0; index < children.Count; index++)
            {
                var (childIndex, process, outputTask, errorTask) = children[index];
                process.WaitForExit();
                var childOutput = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();
                // Each shard reports its own CPU/memory before exiting; a
                // child's TotalProcessorTime is not readable after exit.
                ParseShardMetrics(childOutput, ref childrenCpu, ref childrenAllocatedMb, ref peakMemoryBytes);
                if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                {
                    failed = true;
                    Console.Error.WriteLine($"shard {childIndex} exited with {process.ExitCode}: {error.Trim()}");
                }
                process.Dispose();
            }

            var merged = Merge(output, shards);
            if (merged < 0)
            {
                return 1;
            }

            started.Stop();
            Console.WriteLine($"Merged {merged} samples into {Path.Combine(output, "final.csv")}");
            Console.WriteLine($"Sweep finished in {started.Elapsed.TotalSeconds:F1} s");

            var wallSeconds = started.Elapsed.TotalSeconds;
            var cpuAfter = Process.GetCurrentProcess().TotalProcessorTime;
            var totalCpuSeconds = (cpuAfter - cpuBefore + childrenCpu).TotalSeconds;
            var allocatedMb = childrenAllocatedMb +
                (GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore) / (1024.0 * 1024.0);
            try { peakMemoryBytes = Math.Max(peakMemoryBytes, Process.GetCurrentProcess().PeakWorkingSet64); } catch { }
            var logicalCpus = Environment.ProcessorCount;
            var utilization = wallSeconds > 0
                ? totalCpuSeconds / wallSeconds / logicalCpus * 100.0
                : 0;
            Console.WriteLine($"CPU: {totalCpuSeconds:F1} s busy / {wallSeconds:F1} s wall " +
                              $"= {utilization:F1}% of {logicalCpus} logical cores (parent + shards)");
            Console.WriteLine($"Memory: peak working set {peakMemoryBytes / (1024.0 * 1024.0):F0} MB, " +
                              $"managed allocations {allocatedMb:F0} MB");
            return failed ? 2 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 1;
        }
    }

    private static Process StartShard(string[] args, int index, int count, int workers, string dir)
    {
        var startInfo = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Under `dotnet run` the process is the host, so the entry assembly has
        // to be forwarded explicitly. Under the apphost the managed entry is
        // already implied and forwarding it would shift every argument.
        var host = Environment.ProcessPath ?? "dotnet";
        var isDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet",
            StringComparison.OrdinalIgnoreCase);
        if (isDotnetHost)
        {
            var entry = Environment.GetCommandLineArgs().FirstOrDefault();
            if (!string.IsNullOrEmpty(entry) && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add(entry);
            }
        }

        startInfo.ArgumentList.Add("sweep");
        AppendForwarded(args, startInfo.ArgumentList);
        startInfo.ArgumentList.Add("--shard");
        startInfo.ArgumentList.Add($"{index}/{count}");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(dir);
        startInfo.ArgumentList.Add("--workers");
        startInfo.ArgumentList.Add(workers.ToString(CultureInfo.InvariantCulture));

        startInfo.Environment["MQTTY_WORKERS"] = workers.ToString(CultureInfo.InvariantCulture);
        // One worker per simulation does not benefit from the server GC heaps
        // and the extra threads only add memory pressure when shards run side by
        // side.
        startInfo.Environment["DOTNET_gcServer"] = workers >= 4 ? "1" : "0";

        // Propagate the parallel-publish opt-in to shard children so they use
        // the same PublishAll code path as the parent would.
        var parallelPublish = Environment.GetEnvironmentVariable("MQTTY_PARALLEL_PUBLISH");
        if (!string.IsNullOrEmpty(parallelPublish))
        {
            startInfo.Environment["MQTTY_PARALLEL_PUBLISH"] = parallelPublish;
        }

        if (Environment.GetEnvironmentVariable("MQTTY_SWEEP_DEBUG") == "1")
        {
            Console.Error.WriteLine($"shard {index}: {startInfo.FileName} {string.Join(' ', startInfo.ArgumentList)}");
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start a sweep shard.");
    }

    /// <summary>Forwards every argument except the parent-only ones.</summary>
    private static void AppendForwarded(string[] args, ICollection<string> target)
    {
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            }

            var name = args[index][2..];
            if (name is "parallel" or "shard" or "output" or "workers")
            {
                continue;
            }

            target.Add(args[index]);
            target.Add(args[index + 1]);
        }
    }

    /// <summary>Reads the MQTTY_METRICS line printed by each shard child.</summary>
    private static void ParseShardMetrics(string output, ref TimeSpan cpu, ref double allocatedMb,
        ref long peakMemoryBytes)
    {
        const string marker = "MQTTY_METRICS";
        var index = output.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        var end = output.IndexOf('\n', index);
        var line = end < 0 ? output[index..] : output[index..end];
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = token[..separator];
            if (!double.TryParse(token[(separator + 1)..], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            switch (key)
            {
                case "cpu_s":
                    cpu += TimeSpan.FromSeconds(value);
                    break;
                case "peak_mb":
                    peakMemoryBytes = Math.Max(peakMemoryBytes, (long)(value * 1024 * 1024));
                    break;
                case "alloc_mb":
                    allocatedMb += value;
                    break;
            }
        }
    }

    private static int Merge(string output, int shards)
    {
        var header = "Id;GroupId;IsParallel;EnabledCounters;Brokers;Nets;Clients;Repeats;VirtualTime;RealTime;RealTime (ms);TotalTicks;TotalSteps;Errors;Memory (Mb)";
        var rows = new List<(int Id, string Line)>();
        for (var index = 0; index < shards; index++)
        {
            var path = Path.Combine(output, $"shard-{index}", "final.csv");
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Missing shard output: {path}");
                return -1;
            }

            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Id;", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf(';');
                var id = separator > 0 && int.TryParse(line[..separator], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : int.MaxValue;
                rows.Add((id, line));
            }
        }

        rows.Sort(static (left, right) => left.Id.CompareTo(right.Id));
        var merged = new StringBuilder();
        merged.AppendLine(header);
        foreach (var row in rows)
        {
            merged.AppendLine(row.Line);
        }

        File.WriteAllText(Path.Combine(output, "final.csv"), merged.ToString());
        return rows.Count;
    }

    private static int CheckedProduct(params int[] values)
    {
        long product = 1;
        foreach (var value in values)
        {
            product *= Math.Max(1, value);
            if (product > int.MaxValue)
            {
                throw new ArgumentException("The sweep is too large.");
            }
        }

        return (int)product;
    }

    private static int PhysicalCores()
    {
        var logical = Environment.ProcessorCount;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var coresPerSocket = lines.Where(line => line.StartsWith("cpu cores", StringComparison.Ordinal))
                    .Select(line => int.TryParse(line.Split(':')[1].Trim(), out var value) ? value : 0)
                    .FirstOrDefault();
                var sockets = lines.Where(line => line.StartsWith("physical id", StringComparison.Ordinal))
                    .Select(line => int.TryParse(line.Split(':')[1].Trim(), out var value) ? value : -1)
                    .Distinct()
                    .Count();
                if (coresPerSocket > 0 && sockets > 0)
                {
                    return Math.Clamp(coresPerSocket * sockets, 1, logical);
                }
            }
        }
        catch
        {
            // Fall back to logical cores.
        }

        return logical;
    }

    private static int IndexOf(string[] args, string name)
    {
        // Step by one, not two: the relay app accepts a positional mode word
        // (for example `original`) before the --name value pairs, which would
        // shift a stride-of-two scan off the pairs.
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--" + name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? Value(string[] args, string name)
    {
        var index = IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int? ValueInt(string[] args, string name) =>
        int.TryParse(Value(args, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    private static int[]? IntList(string[] args, string name)
    {
        var value = Value(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var result = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
        return result.Length == 0 ? null : result;
    }

    private static void PrintUsage() => Console.Error.WriteLine(
        "Usage: EntityFX.MqttY.MqttRelay.App sweep [--parallel <shards>] [--workers <per-simulation threads>]\n" +
        "       [--brokers 4,5,10,15] [--nets 2,5,10,50] [--clients 3,10,50] [--repeats 10,100] [--strategies 0,1]\n" +
        "       [--output <directory>]");
}
