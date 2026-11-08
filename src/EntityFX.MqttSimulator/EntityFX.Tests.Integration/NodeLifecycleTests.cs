using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class NodeLifecycleTests
{
    private static (NetworkSimulator Graph, Network Network, TicksOptions Ticks) Build()
    {
        var ticks = new TicksOptions
        {
            TickPeriod = TimeSpan.FromMilliseconds(1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 10
        };
        var graph = new NetworkSimulator(
            new DijkstraPathFinder(),
            new NetworkLogger(false, ticks.TickPeriod, new MonitoringIgnoreOption()),
            ticks,
            enableCounters: false);
        var network = new Network(0, "net", "net", "eth",
            new NetworkOptions { CapacityBytesPerSecond = 1_000_000, TransferTicks = 1 },
            ticks, enableCounters: false);
        graph.AddNetwork(network);
        return (graph, network, ticks);
    }

    [TestMethod]
    public void ServerStop_RemovesByNodeName()
    {
        var (_, network, ticks) = Build();
        var server = new Server(1, "server", "server-address", "test", "test", ticks, enableCounters: false);
        server.Network = network;

        server.Start();
        Assert.IsTrue(server.IsStarted);
        Assert.IsTrue(network.Servers.ContainsKey("server"));

        server.Stop();

        Assert.IsFalse(server.IsStarted);
        Assert.IsFalse(network.Servers.ContainsKey("server"));
    }

    [TestMethod]
    public void ApplicationStop_RemovesByNodeName()
    {
        var (_, network, ticks) = Build();
        var application = new Application<object>(2, "app", "app-address", "test", "test",
            ticks, null, enableCounters: false);
        application.Network = network;

        application.Start();
        Assert.IsTrue(application.IsStarted);
        Assert.IsTrue(network.Applications.ContainsKey("app"));

        application.Stop();

        Assert.IsFalse(application.IsStarted);
        Assert.IsFalse(network.Applications.ContainsKey("app"));
    }
}
