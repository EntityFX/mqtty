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
                PrintStats(networkSimulator, false, brokerc, netc, clientc, repeatc);

                results.Add(new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, false, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors)));

                PrintBefore(true, brokerc, netc, clientc, repeatc, false);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, false);
                PrintStats(networkSimulator, true, brokerc, netc, clientc, repeatc);

                results.Add(new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, false),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors)));

                PrintBefore(true, brokerc, netc, clientc, repeatc, true);
                networkSimulator = mqttRelayApp.ExecuteSimulation(true, brokerc, netc, clientc, repeatc, true);
                PrintStats(networkSimulator, true, brokerc, netc, clientc, repeatc);

                results.Add(new ResultItem(
                    new InParams(brokerc, netc, clientc, repeatc, true, true),
                    new OutParams(networkSimulator.VirtualTime, networkSimulator.RealTime, networkSimulator.TotalTicks, networkSimulator.TotalSteps, networkSimulator.Errors)));

                var table = PrettyPrinterHelper.PrintAsTable(
                    new PrettyPrinterHelper.Header(
                        new PrettyPrinterHelper.Columns(new Dictionary<string, PrettyPrinterHelper.Column>()
                        {
                            ["Parallel"] = new PrettyPrinterHelper.Column("Parallel", 8, 0),
                            ["Brokers"] = new PrettyPrinterHelper.Column("Brokers", 8, 0),
                            ["Nets"] = new PrettyPrinterHelper.Column("Net Length", 10, 0),
                            ["Clients"] = new PrettyPrinterHelper.Column("Clients", 8, 0),
                            ["Repeats"] = new PrettyPrinterHelper.Column("Repeats", 8, 0, DoubleColumnLine: true),
                            ["VirtualTime"] = new PrettyPrinterHelper.Column("Virt Time", 9, 0),
                            ["RealTime"] = new PrettyPrinterHelper.Column("Real Time", 9, 0),
                            ["Ticks"] = new PrettyPrinterHelper.Column("Ticks", 8, 0),
                            ["Steps"] = new PrettyPrinterHelper.Column("Steps", 10, 0),
                            ["Errors"] = new PrettyPrinterHelper.Column("Errors", 6, 0),
                        })), results.Select(r => new PrettyPrinterHelper.Row(
                            new PrettyPrinterHelper.Item[] {
                                new PrettyPrinterHelper.Item("Parallel", r.In.IsParallel),
                                new PrettyPrinterHelper.Item("Brokers", r.In.Brokers),
                                new PrettyPrinterHelper.Item("Nets", r.In.Nets),
                                new PrettyPrinterHelper.Item("Clients", r.In.Clients),
                                new PrettyPrinterHelper.Item("Repeats", r.In.Repeats),
                                new PrettyPrinterHelper.Item("VirtualTime", r.Out.VirtualTime),
                                new PrettyPrinterHelper.Item("RealTime", r.Out.RealTime),
                                new PrettyPrinterHelper.Item("Ticks", r.Out.TotalTicks),
                                new PrettyPrinterHelper.Item("Steps", r.Out.TotalSteps),
                                new PrettyPrinterHelper.Item("Errors", r.Out.Errors),
                            })).ToArray());

                Console.WriteLine(table);
            }
        }
    }
}


static void PrintBefore(bool parallel, int b, int n, int c, int r, bool counters)
{
    Console.WriteLine("Brokers = {0}, Net length = {1}, Clients = {2}, Repeats = {3}", b, n, c, r);
    Console.WriteLine("Is parallel: {0}", parallel);
    Console.WriteLine("Couners enabled: {0}", counters);
}


static void PrintStats(EntityFX.MqttY.Contracts.Network.INetworkSimulator networkSimulator,
    bool parallel, int b, int n, int c, int r)
{
    Console.WriteLine("Virtual time: {0}", networkSimulator.VirtualTime);
    Console.WriteLine("Real time: {0}", networkSimulator.RealTime);
    Console.WriteLine("Ticks: {0}", networkSimulator.TotalTicks);
    Console.WriteLine("Steps: {0}", networkSimulator.TotalSteps);
    Console.WriteLine("Errors: {0}", networkSimulator.Errors);
    Console.WriteLine("----------");
}

//Console.WriteLine(networkSimulator.Counters.PrintCounters());

