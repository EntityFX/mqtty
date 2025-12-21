using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Primitives;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;




if (args.Length > 0)
{
    ProcessResults(args[0]);
    return;
}

var date = DateTime.Now.ToString("yyyy_MM_dd__HH_mm_ss");
var resultPath = Path.Combine("results", date);
FileExtensions.CreateDirectory(resultPath);

var brokers = new[] { 4, 5, 10, 15 };
var netLength = new[] { 2, 5, 10, 50 };
var clients = new[] { 3, 10, 50 };
var repeats = new[] { 10, 100 };

var results = new List<ResultItem>();

var mqttRelayApp = new MqttRelayApp();

var table = string.Empty;

var id = 0;
var gid = 0;
for (int b = 0; b < brokers.Length; b++)
{
    for (int n = 0; n < netLength.Length; n++)
    {
        for (int c = 0; c < clients.Length; c++)
        {
            for (int r = 0, ix = 0; r < repeats.Length; r++, ix++)
            {
                var brokerc = brokers[b];
                var netc = netLength[n];
                var clientc = clients[c];
                var repeatc = repeats[r];

                var prefixBase = $"{ix}__b_{brokerc}__n_{netc}__c_{clientc}__r_{repeatc}";

                PrintBefore(false, brokerc, netc, clientc, repeatc, false);
                var networkSimulator = mqttRelayApp.ExecuteSimulation(false, brokerc, netc, clientc, repeatc, false);

                var ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;

                var result = new ResultItem(id, gid, 
                    new InParams(brokerc, netc, clientc, repeatc, false, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), false);

                PrintStatsForItem(result);
                SaveCounters(resultPath, prefixBase, networkSimulator);
                SaveGraphML(resultPath, prefixBase, networkSimulator);

                results.Add(result);
                networkSimulator.Clear();
                id++;
                GC.Collect();

                PrintBefore(true, brokerc, netc, clientc, repeatc, false);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, false);

                ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                result = new ResultItem(id, gid,
                    new InParams(brokerc, netc, clientc, repeatc, true, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), false);
                PrintStatsForItem(result);
                SaveCounters(resultPath, prefixBase, networkSimulator);
                results.Add(result);
                networkSimulator.Clear();
                id++;
                GC.Collect();

                PrintBefore(true, brokerc, netc, clientc, repeatc, true);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, true);

                ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                result = new ResultItem(id, gid,
                    new InParams(brokerc, netc, clientc, repeatc, true, true),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), true);
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
                ["Memory"] = new PrettyPrinterHelper.Column("Mem (Mb)", 8, 0, Format: "f1"),
            }),
            new PrettyPrinterHelper.Columns(new Dictionary<string, PrettyPrinterHelper.Column>()
            {
                ["In"] = new PrettyPrinterHelper.Column("In", 8 + 8 + 8 + 10 + 8 + 8 + 15, 0, DoubleColumnLine: true),
                ["Out"] = new PrettyPrinterHelper.Column("Out", 9 + 9 + 8 + 10 + 6 + 9 + 11 + 20, 0)
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