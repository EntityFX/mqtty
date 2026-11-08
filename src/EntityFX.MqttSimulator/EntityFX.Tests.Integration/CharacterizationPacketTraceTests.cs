using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

/// <summary>
/// S0 characterization: locks the observable packet lifecycle (logical send,
/// network enqueue and node delivery ticks) before the core refactoring.
/// The trace is sorted by tick/kind/id, so it is stable for sequential runs and
/// for a one-worker parallel pool; cross-network parallel scheduling is covered
/// separately by the metric parity tests.
/// </summary>
[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class CharacterizationPacketTraceTests
{
    [TestMethod]
    public void PacketTrace_SequentialStrategy0_MatchesGolden()
    {
        var actual = RunScenario(parallel: false, strategy: 0);
        AssertGolden("core-packet-trace-seq0.golden.ndjson", actual);
    }

    [TestMethod]
    public void PacketTrace_SequentialStrategy1_MatchesGolden()
    {
        var actual = RunScenario(parallel: false, strategy: 1);
        AssertGolden("core-packet-trace-seq1.golden.ndjson", actual);
    }

    [TestMethod]
    public void PacketTrace_ParallelOneWorkerStrategy0_MatchesGolden()
    {
        var actual = RunScenario(parallel: true, strategy: 0);
        AssertGolden("core-packet-trace-par1.golden.ndjson", actual);
    }

    [TestMethod]
    public void PacketTrace_IsDeterministic()
    {
        var first = RunScenario(parallel: false, strategy: 0);
        var second = RunScenario(parallel: false, strategy: 0);
        Assert.AreEqual(first, second, "Sequential packet trace must be deterministic.");
    }

    private static string RunScenario(bool parallel, int strategy)
    {
        var ticks = new TicksOptions
        {
            TickPeriod = TimeSpan.FromSeconds(1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };
        var graph = new NetworkSimulator(
            new DijkstraPathFinder(),
            new NetworkLogger(false, ticks.TickPeriod, new MonitoringIgnoreOption()),
            ticks,
            enableCounters: false)
        {
            MaxDegreeOfParallelism = 1
        };

        var first = new Network(0, "first", "first", "eth",
            new NetworkOptions { CapacityBytesPerSecond = 1_000_000, TransferTicks = 1 }, ticks, enableCounters: false);
        var second = new Network(1, "second", "second", "eth",
            new NetworkOptions { CapacityBytesPerSecond = 1_000_000, TransferTicks = 1 }, ticks, enableCounters: false);
        graph.AddNetwork(first);
        graph.AddNetwork(second);
        graph.Link("first", "second");
        graph.UpdateRoutes();

        var source = new Server(2, "source", "source", "test", "test", ticks, enableCounters: false);
        var destination = new Server(3, "destination", "destination", "test", "test", ticks, enableCounters: false);
        first.AddServer(source);
        second.AddServer(destination);
        graph.AddServer(source);
        graph.AddServer(destination);

        var received = new List<INetworkPacket>();
        source.PacketReceived += (_, packet) => received.Add(packet);
        destination.PacketReceived += (_, packet) => received.Add(packet);

        var tracePath = Path.Combine(Path.GetTempPath(), $"mqtty-trace-{Guid.NewGuid():N}.ndjson");
        PacketTrace.Start(tracePath);
        try
        {
            source.Send(Packet(1, "source", "destination"));
            source.Send(Packet(2, "source", "source"));
            destination.Send(Packet(3, "destination", "source"));

            for (var i = 0; i < 10; i++)
            {
                Assert.IsTrue(graph.Refresh(parallel, strategy), $"Refresh failed at tick {i}.");
            }
        }
        finally
        {
            PacketTrace.Stop();
        }

        Assert.AreEqual(3, received.Count, "All three packets must be delivered exactly once.");
        try
        {
            return Normalize(File.ReadAllText(tracePath));
        }
        finally
        {
            File.Delete(tracePath);
        }
    }

    private static INetworkPacket Packet(long id, string from, string to) =>
        new NetworkPacket<int>(id, null, 0, from, to, NodeType.Server, NodeType.Server,
            0, 0, new[] { (byte)id }, "test", 0, 1);

    private static void AssertGolden(string fileName, string actual)
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        if (Environment.GetEnvironmentVariable("MQTTY_TRACE_UPDATE") == "1")
        {
            var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            var sourcePath = Path.Combine(projectDirectory, "Fixtures", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, actual + Environment.NewLine);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, actual + Environment.NewLine);
        }

        Assert.IsTrue(File.Exists(outputPath), $"Golden trace not found: {outputPath}. Run with MQTTY_TRACE_UPDATE=1.");
        Assert.AreEqual(Normalize(File.ReadAllText(outputPath)), actual,
            $"Packet trace changed; compare with {fileName}.");
    }

    private static string Normalize(string trace) => string.Join("\n",
        trace.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
}
