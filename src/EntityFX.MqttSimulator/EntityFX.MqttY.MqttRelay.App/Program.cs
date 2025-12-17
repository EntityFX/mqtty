using EntityFX.MqttY.Helper;



var brokers = new[] { 4, 5, 10, 15 };
var netLength = new[] { 2, 5, 10, 50 };
var clients = new[] { 3, 10, 50 };
var repeats = new[] { 100 };

var results = new List<ResultItem>();

var mqttRelayApp = new MqttRelayApp();

for (int b = 0; b < brokers.Length; b++)
{
    for (int n = 0; n < netLength.Length; n++)
    {
        for (int c = 0; c < clients.Length; c++)
        {
            for (int r = 0; r < repeats.Length; r++)
            {
                var brokerc = brokers[b];
                var netc = netLength[n];
                var clientc = clients[c];
                var repeatc = repeats[r];

                PrintBefore(false, brokerc, netc, clientc, repeatc, false);
                var networkSimulator = mqttRelayApp.ExecuteSimulation(false, brokerc, netc, clientc, repeatc, false);

                var result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, false, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors), false);

                PrintStatsForItem(result);
                results.Add(result);
                networkSimulator.Clear();



                PrintBefore(true, brokerc, netc, clientc, repeatc, false);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, false);
                result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors), false);
                PrintStatsForItem(result);
                results.Add(result);
                networkSimulator.Clear();

                PrintBefore(true, brokerc, netc, clientc, repeatc, true);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, true);
                result = new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, true),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors), true);
                PrintStatsForItem(result);
                results.Add(result);
                networkSimulator.Clear();

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
                            ["Ste"] = new PrettyPrinterHelper.Column("Step (ms)", 9, 0, Format: "f5"),
                            ["Errors"] = new PrettyPrinterHelper.Column("Errors", 6, 0),
                        }),
                        new PrettyPrinterHelper.Columns(new Dictionary<string, PrettyPrinterHelper.Column>()
                        {
                            ["In"] = new PrettyPrinterHelper.Column("In", 8 + 8 + 8 + 10 + 8 + 8 + 15, 0, DoubleColumnLine: true),
                            ["Out"] = new PrettyPrinterHelper.Column("Out", 9 + 9 + 8 + 10 + 6 + 9 + 19, 0)
                        })
                        ), results.Select(r => new PrettyPrinterHelper.Row(
                            new PrettyPrinterHelper.Item[] {
                                new PrettyPrinterHelper.Item("Parallel", r.In.IsParallel),
                                new PrettyPrinterHelper.Item("Parallel", r.In.EnabledCounters),
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
                            }, r.rowLine)).ToArray());

                Console.WriteLine(table);

                GC.Collect();
            }
        }
    }
}

void PrintStats(TimeSpan v, TimeSpan r, long t, long s, long e)
{
    Console.WriteLine("Virtual time: {0}", v);
    Console.WriteLine("Real time: {0}", r);
    Console.WriteLine("Ticks: {0}", t);
    Console.WriteLine("Steps: {0}", s);
    Console.WriteLine("Errors: {0}", e);
    Console.WriteLine("----------");
}

void PrintStatsForItem(ResultItem result)
{
    PrintStats(result.Out.VirtualTime, result.Out.RealTime, result.Out.TotalTicks, result.Out.TotalSteps, result.Out.Errors);
}

static void PrintBefore(bool parallel, int b, int n, int c, int r, bool counters)
{
    Console.WriteLine("Brokers = {0}, Net length = {1}, Clients = {2}, Repeats = {3}", b, n, c, r);
    Console.WriteLine("Is parallel: {0}", parallel);
    Console.WriteLine("Couners enabled: {0}", counters);
}




//Console.WriteLine(networkSimulator.Counters.PrintCounters());

