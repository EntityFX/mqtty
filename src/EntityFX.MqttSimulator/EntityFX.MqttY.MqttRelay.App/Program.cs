using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using System.Diagnostics;
using System.Globalization;
using System.Text;


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

for (int b = 0; b < brokers.Length; b++)
{
    for (int n = 0; n < netLength.Length; n++)
    {
        for (int c = 0; c < clients.Length; c++)
        {
            for (int r = 0, ix = 0; r < repeats.Length; r++, ix++)
            {
                var prefixBase = $"{ix}__b_{b}__n_{n}__c_{c}__r_{r}";

                var brokerc = brokers[b];
                var netc = netLength[n];
                var clientc = clients[c];
                var repeatc = repeats[r];

                PrintBefore(false, brokerc, netc, clientc, repeatc, false);
                var networkSimulator = mqttRelayApp.ExecuteSimulation(false, brokerc, netc, clientc, repeatc, false);

                var ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;

                var result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, false, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), false);

                PrintStatsForItem(result);
                SaveCounters(resultPath, prefixBase, networkSimulator);

                results.Add(result);
                networkSimulator.Clear();

                GC.Collect();

                PrintBefore(true, brokerc, netc, clientc, repeatc, false);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, false);

                ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), false);
                PrintStatsForItem(result);
                SaveCounters(resultPath, prefixBase, networkSimulator);
                results.Add(result);
                networkSimulator.Clear();

                GC.Collect();

                PrintBefore(true, brokerc, netc, clientc, repeatc, true);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, true);

                ws = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
                result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, true),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors, ws), true);
                PrintStatsForItem(result);
                SaveCounters(resultPath, prefixBase, networkSimulator);
                results.Add(result);
                networkSimulator.Clear();

                GC.Collect();

                table = PrintTable(results);
                SaveTableAsFile(resultPath, prefixBase, table);
                SaveResultsToCsv(resultPath, prefixBase, results);

            }
        }
    }
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

    sb.Append($"IsParallel;");
    sb.Append($"EnabledCounters;");
    sb.Append($"Brokers;");
    sb.Append($"Nets;");
    sb.Append($"Clients;");
    sb.Append($"Repeats;");
    sb.Append($"VirtualTime;");
    sb.Append($"RealTime;");
    sb.Append($"TotalTicks;");
    sb.Append($"TotalSteps;");
    sb.Append($"Errors;");
    sb.Append($"Memory (Mb)");
    sb.AppendLine();

    foreach (var result in results)
    {
        sb.Append($"{result.In.IsParallel};");
        sb.Append($"{result.In.EnabledCounters};");
        sb.Append($"{result.In.Brokers};");
        sb.Append($"{result.In.Nets};");
        sb.Append($"{result.In.Clients};");
        sb.Append($"{result.In.Repeats};");
        sb.Append($"{result.Out.VirtualTime};");
        sb.Append($"{result.Out.RealTime};");
        sb.Append($"{result.Out.TotalTicks};");
        sb.Append($"{result.Out.TotalSteps};");
        sb.Append($"{result.Out.Errors};");
        sb.Append(result.Out.MemoryWorkingSet.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    var filePath = Path.Combine(path, prefixBase);
    File.WriteAllText($"{filePath}.csv", sb.ToString());
}
