using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.MqttRelay.App;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Primitives;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

// Print help when --help or -h is passed.
if (args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
    args[0].Equals("-h", StringComparison.OrdinalIgnoreCase) ||
    args[0].Equals("/?", StringComparison.OrdinalIgnoreCase))
{
    PrintHelp();
    return;
}

if (args.Length > 0 && args[0].Equals("calibrate", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = MqttRelayExperimentCommand.RunCalibration(args.Skip(1).ToArray());
    return;
}

if (args.Length > 0 && args[0].Equals("experiment", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = MqttRelayExperimentCommand.Run(args.Skip(1).ToArray());
    return;
}

// Relay benchmark on the production FederationRunner path (FederationMode.Relay).
// This is the same relay architecture the reference benchmark measures: a
// continuous credit load driven through MqttRelay applications, which is where
// the parallel refresh reaches ~7.6x.
if (args.Length > 0 && args[0].Equals("benchmark", StringComparison.OrdinalIgnoreCase))
{
    Environment.ExitCode = RelayBenchmarkCommand.Run(args.Skip(1).ToArray());
    return;
}

// Coarse-grained parallelism: independent simulations are embarrassingly
// parallel, so the sweep is sharded across processes. Every shard runs the
// same, unmodified bulk-synchronous model, which keeps the results identical
// to a single-process run.
if (args.Length > 0 && args[0].Equals("sweep", StringComparison.OrdinalIgnoreCase))
{
    var sweepArgs = args.Skip(1).ToArray();
    if (!RelaySweepCommand.HasShard(sweepArgs))
    {
        Environment.ExitCode = RelaySweepCommand.Run(sweepArgs);
        return;
    }

    // Shard child: fall through to the ordinary sweep with the shard filter.
    args = sweepArgs;
}


if (args.Length > 0 && args[0] != "original" && !args[0].StartsWith("--", StringComparison.Ordinal))
{
    ProcessResults(args[0]);
    return;
}

var date = DateTime.Now.ToString("yyyy_MM_dd__HH_mm_ss");
var shard = RelaySweepCommand.ReadShard(args);
var resultPath = shard.OutputPath ?? Path.Combine("results", date);
FileExtensions.CreateDirectory(resultPath);

// ---- Multi-core defaults (apply the measured acceleration by default) ----
// Workers: one per physical core is the sweet spot on this hardware (12C/24T).
// More workers than physical cores means SMT oversubscription and spin-barrier
// collapse; fewer leaves cores idle.
// NodeChunk=1: fine-grained node claims avoid a heavy tail on uneven nodes.
// Barrier=spin: the dissemination spin fence is fastest at workers <= physical.
// The user can override any of these via --workers/--node-chunk/--barrier or
// the MQTTY_* environment variables.
ApplyParallelDefaults(args);

var brokers = RelaySweepCommand.Brokers(args);
var netLength = RelaySweepCommand.Nets(args);
var clients = RelaySweepCommand.Clients(args);
var repeats = RelaySweepCommand.Repeats(args);
var refreshStrategies = RelaySweepCommand.Strategies(args);
int? bytes = null;

TicksOptions? tickOptions = null;

NetworkOptions? networkOptions = null;

if (args.Length > 0 && args[0] == "original")
{
    brokers = new[] { 4 };
    clients = new[] { 15 };
    repeats = new[] { 131 };
    netLength = new[] { 1 };
    refreshStrategies = new[] { 0 };
    bytes = 8;

    tickOptions = new TicksOptions()
    {
        OutgoingWaitTicks = 5,
        TickPeriod = TimeSpan.FromMilliseconds(0.1)
    };
}

var results = new List<ResultItem>();

var mqttRelayApp = new MqttRelayApp(tickOptions, networkOptions);

var table = string.Empty;

var id = 0;
var gid = 0;
var configOrdinal = 0;
for (int b = 0; b < brokers.Length; b++)
{
    for (int n = 0; n < netLength.Length; n++)
    {
        for (int c = 0; c < clients.Length; c++)
        {
            for (int r = 0; r < repeats.Length; r++)
            {
                for (int rs = 0, ix = 0; rs < refreshStrategies.Length; rs++, ix++)
                {
                    // The ordinal is stable across shards, so ids and group ids
                    // match a single-process run and the merged CSV can be
                    // post-processed exactly as before.
                    var ordinal = configOrdinal++;
                    if (!shard.Contains(ordinal))
                    {
                        continue;
                    }

                    var brokerc = brokers[b];
                    var netc = netLength[n];
                    var clientc = clients[c];
                    var repeatc = repeats[r];
                    id = ordinal * 3;
                    gid = ordinal;

                    var prefixBase = $"{ix}__b_{brokerc}__n_{netc}__c_{clientc}__r_{repeatc}";

                    PrintBefore(false, brokerc, netc, clientc, repeatc, false);
                    var (networkSimulator, cpuPercent) = RunMeasured(false, brokerc, netc, clientc, repeatc, false, rs);

                    var ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;

                    var result = new ResultItem(id, gid,
                        new InParams(brokerc, netc, clientc, repeatc, false, false, rs),
                        new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws, cpuPercent), false);

                    PrintStatsForItem(result);
                    SaveCounters(resultPath, prefixBase, networkSimulator);
                    SaveGraphML(resultPath, prefixBase, networkSimulator);

                    results.Add(result);
                    networkSimulator.Clear();
                    id++;
                    GC.Collect();

                    PrintBefore(true, brokerc, netc, clientc, repeatc, false);
                    (networkSimulator, cpuPercent) = RunMeasured(true, brokerc, netc, clientc, repeatc, false, rs);

                    ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                    result = new ResultItem(id, gid,
                        new InParams(brokerc, netc, clientc, repeatc, true, false, rs),
                        new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws, cpuPercent), false);
                    PrintStatsForItem(result);
                    SaveCounters(resultPath, prefixBase, networkSimulator);
                    results.Add(result);
                    networkSimulator.Clear();
                    id++;
                    GC.Collect();

                    PrintBefore(true, brokerc, netc, clientc, repeatc, true);
                    (networkSimulator, cpuPercent) = RunMeasured(true, brokerc, netc, clientc, repeatc, true, rs);

                    ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                    result = new ResultItem(id, gid,
                        new InParams(brokerc, netc, clientc, repeatc, true, true, rs),
                        new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws, cpuPercent), true);
                    PrintStatsForItem(result);
                    SaveCounters(resultPath, prefixBase, networkSimulator);
                    results.Add(result);
                    networkSimulator.Clear();
                    id++;
                    gid++;
                    GC.Collect();

                    table = PrintTable(results);
                    SaveTableAsFile(resultPath, prefixBase, table);
                    SaveResultsToCsv(resultPath, prefixBase, results);

                }
            }
        }
    }
}

(INetworkSimulator Simulator, double CpuPercent) RunMeasured(
    bool isParallel, int b, int n, int c, int r, bool counters, int strategy)
{
    var process = Process.GetCurrentProcess();
    var cpuBefore = process.TotalProcessorTime;
    var stopwatch = Stopwatch.StartNew();
    var simulator = mqttRelayApp.ExecuteSimulation(isParallel, b, n, c, r, counters, strategy, bytes);
    stopwatch.Stop();
    var cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore).TotalSeconds;
    var logical = Environment.ProcessorCount;
    var percent = stopwatch.Elapsed.TotalSeconds > 0
        ? cpuSeconds / stopwatch.Elapsed.TotalSeconds / logical * 100.0
        : 0;
    return (simulator, percent);
}

void SaveGraphML(string resultPath, string prefixBase, INetworkSimulator networkSimulator)
{
    var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
    var graphMl = plantUmlGraphGenerator.SerializeNetworkGraph(networkSimulator);

    var filePath = Path.Combine(resultPath, prefixBase);
    File.WriteAllText($"{filePath}.graphml", graphMl);
}

SaveResultsToCsv(resultPath, "final", results);
table = PrintTable(results);
SaveTableAsFile(resultPath, "final", table);

// Shard children report their own resource usage so the sweep parent can sum
// CPU across processes: a child's TotalProcessorTime is no longer readable
// after it exits. The line is machine-parsable and cross-platform.
if (args.Any(arg => arg.StartsWith("--shard", StringComparison.OrdinalIgnoreCase)))
{
    var process = Process.GetCurrentProcess();
    Console.WriteLine("MQTTY_METRICS cpu_s=" +
        process.TotalProcessorTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) +
        " peak_mb=" + (process.PeakWorkingSet64 / (1024.0 * 1024.0)).ToString("F0", CultureInfo.InvariantCulture) +
        " alloc_mb=" + (GC.GetTotalAllocatedBytes(precise: false) / (1024.0 * 1024.0)).ToString("F0", CultureInfo.InvariantCulture));
}

void PrintStats(TimeSpan v, TimeSpan r, long t, long s, long e, double m)
{
    Console.WriteLine("Virtual time: {0}", v);
    Console.WriteLine("Real time: {0}", r);
    Console.WriteLine("Ticks: {0}", t);
    Console.WriteLine("Steps: {0}", s);
    Console.WriteLine("Errors: {0}", e);
    Console.WriteLine("Memory (Mb): {0}", m);
    Console.WriteLine("----------");
}

void PrintStatsForItem(ResultItem result)
{
    PrintStats(result.Out.VirtualTime, result.Out.RealTime, result.Out.TotalTicks, result.Out.TotalSteps,
        result.Out.Errors, result.Out.MemoryWorkingSet);
}

static void PrintBefore(bool parallel, int b, int n, int c, int r, bool counters)
{
    Console.WriteLine("Brokers = {0}, Net length = {1}, Clients = {2}, Repeats = {3}", b, n, c, r);
    Console.WriteLine("Is parallel: {0}", parallel);
    Console.WriteLine("Couners enabled: {0}", counters);
}

static string PrintTable(List<ResultItem> results)
{
    var table = PrettyPrinterHelper.PrintAsTable(
        new PrettyPrinterHelper.Header(
            new PrettyPrinterHelper.Columns(new Dictionary<string, PrettyPrinterHelper.Column>()
            {
                ["Parallel"] = new PrettyPrinterHelper.Column("Parallel", 8, 0),
                ["Counters"] = new PrettyPrinterHelper.Column("Counters", 8, 0),
                ["Brokers"] = new PrettyPrinterHelper.Column("Brokers", 8, 0),
                ["Nets"] = new PrettyPrinterHelper.Column("Net Length", 10, 0),
                ["Clients"] = new PrettyPrinterHelper.Column("Clients", 8, 0),
                ["Repeats"] = new PrettyPrinterHelper.Column("Repeats", 8, 0, DoubleColumnLine: true),
                ["VirtualTime"] = new PrettyPrinterHelper.Column("Virt Time", 11, 0),
                ["RealTime"] = new PrettyPrinterHelper.Column("Real Time", 11, 0),
                ["Ticks"] = new PrettyPrinterHelper.Column("Ticks", 8, 0),
                ["Steps"] = new PrettyPrinterHelper.Column("Steps", 10, 0),
                ["Ste"] = new PrettyPrinterHelper.Column("Step (ms)", 9, 0, Format: "f5", DoubleColumnLine: true),
                ["Errors"] = new PrettyPrinterHelper.Column("Errors", 6, 0),
                ["Cpu"] = new PrettyPrinterHelper.Column("CPU %", 6, 0, Format: "f1"),
                ["Memory"] = new PrettyPrinterHelper.Column("Mem (Mb)", 8, 0, Format: "f1"),
            }),
            new PrettyPrinterHelper.Columns(new Dictionary<string, PrettyPrinterHelper.Column>()
            {
                ["In"] = new PrettyPrinterHelper.Column("In", 8 + 8 + 8 + 10 + 8 + 8 + 15, 0, DoubleColumnLine: true),
                ["Out"] = new PrettyPrinterHelper.Column("Out", 9 + 9 + 8 + 10 + 6 + 9 + 11 + 20 + 9, 0)
            })
            ), results.Select(r => new PrettyPrinterHelper.Row(
                new PrettyPrinterHelper.Item[] {
                                new PrettyPrinterHelper.Item("Parallel", r.In.IsParallel),
                                new PrettyPrinterHelper.Item("Counters", r.In.EnabledCounters),
                                new PrettyPrinterHelper.Item("Brokers", r.In.Brokers),
                                new PrettyPrinterHelper.Item("Nets", r.In.Nets),
                                new PrettyPrinterHelper.Item("Clients", r.In.Clients),
                                new PrettyPrinterHelper.Item("Repeats", r.In.Repeats),
                                new PrettyPrinterHelper.Item("VirtualTime", r.Out.VirtualTime),
                                new PrettyPrinterHelper.Item("RealTime", r.Out.RealTime),
                                new PrettyPrinterHelper.Item("Ticks", r.Out.TotalTicks),
                                new PrettyPrinterHelper.Item("Steps", r.Out.TotalSteps),
                                new PrettyPrinterHelper.Item("Ste", r.Out.RealTime.TotalMilliseconds / r.Out.TotalSteps),
                                new PrettyPrinterHelper.Item("Errors", r.Out.Errors),
                                new PrettyPrinterHelper.Item("Cpu", r.Out.CpuPercent),
                                new PrettyPrinterHelper.Item("Memory", r.Out.MemoryWorkingSet),
                }, r.rowLine)).ToArray());
    Console.WriteLine(table);

    return table;
}

static void SaveCounters(string path, string prefixBase, INetworkSimulator networkSimulator)
{
    var countersText = networkSimulator.Counters.Dump();
    var filePath = Path.Combine(path, prefixBase);
    File.WriteAllText($"{filePath}.counters.txt", countersText);
}


void SaveTableAsFile(string resultPath, string prefixBase, string table)
{
    var filePath = Path.Combine(resultPath, prefixBase);
    File.WriteAllText($"{filePath}.table.txt", table);
}

void SaveResultsToCsv(string path, string prefixBase, List<ResultItem> results)
{
    var sb = new StringBuilder();

    sb.Append($"Id;");
    sb.Append($"GroupId;");
    sb.Append($"IsParallel;");
    sb.Append($"EnabledCounters;");
    sb.Append($"Brokers;");
    sb.Append($"Nets;");
    sb.Append($"Clients;");
    sb.Append($"Repeats;");
    sb.Append($"VirtualTime;");
    sb.Append($"RealTime;");
    sb.Append($"RealTime (ms);");
    sb.Append($"TotalTicks;");
    sb.Append($"TotalSteps;");
    sb.Append($"Errors;");
    sb.Append($"Memory (Mb)");
    sb.AppendLine();

    foreach (var result in results)
    {
        sb.Append($"{result.Id};");
        sb.Append($"{result.GroupId};");
        sb.Append($"{result.In.IsParallel};");
        sb.Append($"{result.In.EnabledCounters};");
        sb.Append($"{result.In.Brokers};");
        sb.Append($"{result.In.Nets};");
        sb.Append($"{result.In.Clients};");
        sb.Append($"{result.In.Repeats};");
        sb.Append($"{result.Out.VirtualTime};");
        sb.Append($"{result.Out.RealTime};");
        sb.Append($"{result.Out.RealTime.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)};");
        sb.Append($"{result.Out.TotalTicks};");
        sb.Append($"{result.Out.TotalSteps};");
        sb.Append($"{result.Out.Errors};");
        sb.Append(result.Out.MemoryWorkingSet.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    var filePath = Path.Combine(path, prefixBase);
    File.WriteAllText($"{filePath}.csv", sb.ToString());
}


void ProcessResults(string resultPath)
{
    var filePath = resultPath;
    if (!File.Exists(filePath))
    {
        return;
    }

    var items = ReadCsvResult(filePath);

    var date = Path.GetFileNameWithoutExtension(filePath);

    var statsPath = Path.Combine("stats", date);
    FileExtensions.CreateDirectory(statsPath);

    ProcessByClients(items, statsPath);
    ProcessByBrokers(items, statsPath);

}

void ProcessByBrokers(FlatItem[] items, string statsPath)
{
    var uniqueRepeats = items.Select(i => i.Repeats).Distinct().ToArray();
    var uniqueNets = items.Select(i => i.Nets).Distinct().ToArray();
    var uniqueClients = items.Select(i => i.Clients).Distinct().ToArray();
    var uniqueBrokers = items.Select(i => i.Brokers).Distinct().ToArray();

    foreach (var uniqueRepeat in uniqueRepeats)
    {
        var subItems = items.Where(i => i.Repeats == uniqueRepeat);

        foreach (var uniqueNet in uniqueNets)
        {
            foreach (var uniqueClient in uniqueClients)
            {
                var resultItems = subItems.Where(i => i.Nets == uniqueNet).Where(i => i.Clients == uniqueClient)
                    .GroupBy(i => i.GroupId);

                SaveStatsToCsv(statsPath, resultItems, (i) => i.Brokers, $"Nets_{uniqueNet}_Clients_{uniqueClient}", "Brokers");
            }
        }
    }
}

void ProcessByClients(FlatItem[] items, string statsPath)
{
    var uniqueRepeats = items.Select(i => i.Repeats).Distinct().ToArray();
    var uniqueNets = items.Select(i => i.Nets).Distinct().ToArray();
    var uniqueClients = items.Select(i => i.Clients).Distinct().ToArray();
    var uniqueBrokers = items.Select(i => i.Brokers).Distinct().ToArray();

    foreach (var uniqueRepeat in uniqueRepeats)
    {
        var subItems = items.Where(i => i.Repeats == uniqueRepeat);

        foreach (var uniqueNet in uniqueNets)
        {
            foreach (var uniqueBroker in uniqueBrokers)
            {
                var resultItems = subItems.Where(i => i.Nets == uniqueNet).Where(i => i.Brokers == uniqueBroker)
                    .GroupBy(i => i.GroupId);

                SaveStatsToCsv(statsPath, resultItems, (i) => i.Clients, $"Nets_{uniqueNet}_Brokers_{uniqueBroker}", "Clients");
            }
        }
    }
}

void SaveStatsToCsv(string statsPath, IEnumerable<IGrouping<int, FlatItem>> resultItems, Func<FlatItem, object> value, string label, string by)
{
    var sb = new StringBuilder();

    sb.Append($"{by};");
    sb.Append($"Brokers;");
    sb.Append($"Nets;");
    sb.Append($"Clients;");
    sb.Append($"Repeats;");
    sb.Append($"TotalTicks;");
    sb.Append($"VirtualTime;");
    sb.Append($"RealTime [Single];");
    sb.Append($"RealTime [Single] (ms);");
    sb.Append($"RealTime [Parallel];");
    sb.Append($"RealTime [Parallel] (ms);");
    sb.Append($"TotalSteps [Single];");
    sb.Append($"TotalSteps [Parallel];");
    sb.Append($"Memory [Single] (Mb);");
    sb.Append($"Memory [Parallel] (Mb)");
    sb.AppendLine();

    foreach (var item in resultItems)
    {
        var itemOneThread = item.First(i => i.IsParallel == false && i.EnabledCounters == false);
        var itemParallel = item.First(i => i.IsParallel == true && i.EnabledCounters == false);

        sb.Append($"{value.Invoke(itemOneThread).ToString()};");
        sb.Append($"{itemOneThread.Brokers};");
        sb.Append($"{itemOneThread.Nets};");
        sb.Append($"{itemOneThread.Clients};");
        sb.Append($"{itemOneThread.Repeats};");
        sb.Append($"{itemOneThread.TotalTicks};");
        sb.Append($"{itemOneThread.VirtualTime};");
        sb.Append($"{itemOneThread.RealTime};");
        sb.Append($"{itemOneThread.RealTimeMs.ToString(CultureInfo.InvariantCulture)};");
        sb.Append($"{itemParallel.RealTime};");
        sb.Append($"{itemParallel.RealTimeMs.ToString(CultureInfo.InvariantCulture)};");
        sb.Append($"{itemOneThread.TotalSteps};");
        sb.Append($"{itemParallel.TotalSteps};");
        sb.Append($"{itemOneThread.MemoryWorkingSet.ToString(CultureInfo.InvariantCulture)};");
        sb.Append($"{itemParallel.MemoryWorkingSet.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();
    }

    var csv = sb.ToString();

    var csvPath = Path.Combine(statsPath, $"{by}__{label}.csv");
    File.WriteAllText(csvPath, csv);
}

static FlatItem[] ReadCsvResult(string filePath)
{
    var csvLines = File.ReadAllLines(filePath);

    var items = new List<FlatItem>();
    foreach (var csvLine in csvLines)
    {
        var parts = csvLine.Split(';');
        if (parts[0].StartsWith("Id"))
        {
            continue;
        }
        var flatItem = new FlatItem(
            Id: int.Parse(parts[0]),
            GroupId: int.Parse(parts[1]),
            IsParallel: bool.Parse(parts[2]),
            EnabledCounters: bool.Parse(parts[3]),
            Brokers: int.Parse(parts[4]),
            Nets: int.Parse(parts[5]),
            Clients: int.Parse(parts[6]),
            Repeats: int.Parse(parts[7]),
            VirtualTime: TimeSpan.Parse(parts[8]),
            RealTime: TimeSpan.Parse(parts[9]),
            RealTimeMs: double.Parse(parts[10], CultureInfo.InvariantCulture),
            TotalTicks: long.Parse(parts[11]),
            TotalSteps: long.Parse(parts[12]),
            Errors: long.Parse(parts[13]),
            MemoryWorkingSet: double.Parse(parts[14], CultureInfo.InvariantCulture)
            );
        items.Add(flatItem);
        //Id;IsParallel;EnabledCounters;Brokers;Nets;Clients;Repeats;VirtualTime;RealTime;RealTime (ms);TotalTicks;TotalSteps;Errors;Memory (Mb)
    }

    return items.ToArray();
}

// Apply measured multi-core defaults unless the user already set them (via
// MQTTY_* env vars or command-line flags). One refresh worker per physical
// core, nodeChunk=1 (fine-grained claims), and the dissemination spin barrier
// are the settings that gave 7.6× wall speedup on the 12-core reference host.
static void ApplyParallelDefaults(string[] args)
{
    var physicalCores = EntityFX.MqttY.Network.CpuTopology.PhysicalCoreCount();

    if (!HasArg(args, "--workers") &&
        Environment.GetEnvironmentVariable("MQTTY_WORKERS") == null)
    {
        Environment.SetEnvironmentVariable("MQTTY_WORKERS", physicalCores.ToString(CultureInfo.InvariantCulture));
    }

    if (!HasArg(args, "--node-chunk") &&
        Environment.GetEnvironmentVariable("MQTTY_NODE_CHUNK") == null)
    {
        Environment.SetEnvironmentVariable("MQTTY_NODE_CHUNK", "1");
    }

    if (!HasArg(args, "--barrier") &&
        Environment.GetEnvironmentVariable("MQTTY_BARRIER") == null)
    {
        Environment.SetEnvironmentVariable("MQTTY_BARRIER", "spin");
    }
}

static bool HasArg(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }
    return false;
}

static void PrintHelp()
{
    var physicalCores = Environment.ProcessorCount;
    Console.WriteLine("EntityFX.MqttY.MqttRelay.App — MQTT broker/network simulator in model time.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  EntityFX.MqttY.MqttRelay.App [command] [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  benchmark [--brokers N] [--publishers N] [--rps N] [--measure S] [--repeat N]");
    Console.WriteLine("            [--workers W] [--seed N] [--qos N]");
    Console.WriteLine("        Run the production relay path (FederationRunner, Relay mode) seq vs par");
    Console.WriteLine("        and print the wall/refresh speedup on a continuous credit load.");
    Console.WriteLine("  sweep [--parallel N] [--workers W] [--brokers b,...] [--nets n,...]");
    Console.WriteLine("            [--clients c,...] [--repeats r,...] [--strategies s,...]");
    Console.WriteLine("            [--output <dir>]");
    Console.WriteLine("        Run a parameter sweep across independent simulations (default: sequential).");
    Console.WriteLine("  original");
    Console.WriteLine("        Run the legacy single-configuration demo (4 brokers, 15 clients, 131 repeats).");
    Console.WriteLine("  calibrate [args]");
    Console.WriteLine("        Run calibration for relay experiment timing.");
    Console.WriteLine("  experiment [args]");
    Console.WriteLine("        Run a single relay experiment.");
    Console.WriteLine("  <results.csv>");
    Console.WriteLine("        Process an existing results CSV into statistics.");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --brokers N,...     Broker counts (default: 4,5,10,15)");
    Console.WriteLine("  --nets N,...        Networks per node (default: 2,5,10,50)");
    Console.WriteLine("  --clients N,...     Client counts (default: 3,10,50)");
    Console.WriteLine("  --repeats N,...     Publish repeats per configuration (default: 10,100)");
    Console.WriteLine("  --strategies N,...  Refresh strategies (default: 0,1)");
    Console.WriteLine("  --parallel N        Number of concurrent shard processes (default: physical cores)");
    Console.WriteLine("  --workers W         Refresh workers per simulation (default: physical cores)");
    Console.WriteLine("  --node-chunk N      Node claim chunk size (default: 1)");
    Console.WriteLine("  --barrier spin|mon  Tick barrier mode (default: spin)");
    Console.WriteLine("  --output <dir>      Output directory (default: results/<timestamp>)");
    Console.WriteLine();
    Console.WriteLine("Environment variables:");
    Console.WriteLine("  MQTTY_WORKERS        Override refresh worker count");
    Console.WriteLine("  MQTTY_NODE_CHUNK     Override node claim chunk size");
    Console.WriteLine("  MQTTY_BARRIER        Override barrier mode (spin|monitor)");
    Console.WriteLine("  MQTTY_PARALLEL_PUBLISH=1  Enable parallel publish across brokers (opt-in)");
    Console.WriteLine("  MQTTY_DISABLE_COUNTERS=1  Disable counters for faster runs");
    Console.WriteLine();
    Console.WriteLine($"Multi-core defaults: --workers {physicalCores}, --node-chunk 1, --barrier spin");
    Console.WriteLine("  (one worker per physical core; override via --workers/--node-chunk/--barrier");
    Console.WriteLine("   or MQTTY_* environment variables)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  EntityFX.MqttY.MqttRelay.App benchmark --brokers 16 --publishers 64 --rps 2000 --measure 3 --repeat 5");
    Console.WriteLine("  EntityFX.MqttY.MqttRelay.App sweep --parallel 4 --workers 3");
    Console.WriteLine("  EntityFX.MqttY.MqttRelay.App sweep --brokers 16 --nets 5 --clients 10 --repeats 100 --parallel 12 --workers 1");
    Console.WriteLine("  EntityFX.MqttY.MqttRelay.App original");
    Console.WriteLine();
}

