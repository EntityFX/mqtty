using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class DeterministicCoreTests
{
    private static TicksOptions Ticks() => new()
    {
        TickPeriod = TimeSpan.FromSeconds(1), OutgoingWaitTicks = 1,
        ReceiveWaitPeriod = TimeSpan.FromSeconds(30), CounterHistoryDepth = 1000
    };

    private static (NetworkSimulator Graph, Network Network, Server Server) Build(NetworkOptions? options = null, TicksOptions? configuredTicks = null)
    {
        var ticks = configuredTicks ?? Ticks();
        var graph = new NetworkSimulator(new DijkstraPathFinder(),
            new NetworkLogger(false, ticks.TickPeriod, new MonitoringIgnoreOption()), ticks, true);
        var network = new Network(0, "net", "net", "eth",
            options ?? new NetworkOptions { Speed = 1000000, TransferTicks = 1 }, ticks, true);
        graph.AddNetwork(network);
        var server = new Server(1, "server", "server", "test", "test", ticks, true);
        network.AddServer(server);
        graph.AddServer(server);
        return (graph, network, server);
    }

    private static INetworkPacket Packet(long id, int bytes = 1) =>
        new NetworkPacket<int>(id, null, 0, "server", "server", NodeType.Server,
            NodeType.Server, 1, 1, new byte[bytes], "test", 0, 1);

    [TestMethod]
    public void Forwarding_DefersExactPacketWhenDestinationQueueIsFull()
    {
        var (graph, source, _) = Build();
        var ticks = Ticks();
        var destination = new Network(2, "other", "other", "eth",
            new NetworkOptions { CapacityBytesPerSecond = 1000000, QueueCapacity = 1, TransferTicks = 1 }, ticks, true);
        graph.AddNetwork(destination);
        graph.Link("net", "other");
        graph.UpdateRoutes();
        var sink = new Server(3, "sink", "sink", "test", "test", ticks, true);
        destination.AddServer(sink);
        graph.AddServer(sink);
        INetworkPacket first = new NetworkPacket<int>(10, null, 0, "server", "sink", NodeType.Server,
            NodeType.Server, 1, 3, new byte[1], "test", 0, 1);
        INetworkPacket forwarded = new NetworkPacket<int>(11, null, 0, "server", "sink", NodeType.Server,
            NodeType.Server, 1, 3, new byte[1], "test", 0, 1);
        var received = new List<INetworkPacket>();
        sink.PacketReceived += (_, p) => received.Add(p);
        Assert.IsTrue(destination.Send(first));
        Assert.IsTrue(source.Send(forwarded));
        source.Refresh();
        graph.Tick();
        source.Refresh();
        Assert.AreEqual(1L, destination.QueueSize);
        Assert.AreEqual(1L, source.QueueSize);
        destination.Refresh();
        source.Refresh();
        Assert.AreEqual(0L, source.QueueSize);
        Assert.AreEqual(1L, destination.QueueSize);
        graph.Tick();
        destination.Refresh();
        graph.Tick();
        sink.Refresh();
        CollectionAssert.AreEqual(new[] { first, forwarded }, received);
        Assert.AreSame(forwarded, received[1]);
    }

    [TestMethod]
    public void NodeIncoming_DeferredPacketRetainsIdentityWithoutLossOrDuplication()
    {
        var ticks = Ticks();
        var (graph, _, server) = Build(configuredTicks: ticks);
        var ready = Packet(1);
        var deferred = Packet(2);
        var received = new List<INetworkPacket>();
        server.PacketReceived += (_, p) => received.Add(p);
        server.Receive(ready);
        ticks.OutgoingWaitTicks = 3;
        server.Receive(deferred);
        graph.Tick();
        server.Refresh();
        Assert.AreEqual(1, received.Count);
        Assert.AreSame(ready, received[0]);
        server.Refresh();
        Assert.AreEqual(1, received.Count);
        server.Refresh();
        Assert.AreEqual(2, received.Count);
        Assert.AreSame(deferred, received[1]);
        server.Refresh();
        Assert.AreEqual(2, received.Count);
        Assert.IsTrue(graph.IsQuiescent);
    }

    [TestMethod]
    public void NodeRefresh_SnapshotsIncomingQueueBeforeOutgoingCallbacks()
    {
        var ticks = Ticks();
        var graph = new NetworkSimulator(new DijkstraPathFinder(),
            new NetworkLogger(false, ticks.TickPeriod, new MonitoringIgnoreOption()), ticks, true);
        var network = new CallbackNetwork(ticks);
        graph.AddNetwork(network);
        var server = new Server(1, "server", "server", "test", "test", ticks, true);
        network.AddServer(server);
        graph.AddServer(server);
        var incoming = Packet(2);
        var received = new List<INetworkPacket>();
        server.PacketReceived += (_, p) => received.Add(p);
        network.OnSend = () =>
        {
            server.Receive(incoming);
            // External callbacks may advance time; eligibility still starts at the next invocation.
            graph.Tick();
        };
        server.Send(Packet(1));
        graph.Tick();
        server.Refresh();
        Assert.AreEqual(0, received.Count);
        server.Refresh();
        Assert.AreEqual(1, received.Count);
        Assert.AreSame(incoming, received[0]);
    }

    private sealed class CallbackNetwork : Network
    {
        public Action? OnSend { get; set; }
        public CallbackNetwork(TicksOptions ticks) : base(0, "net", "net", "eth",
            new NetworkOptions { CapacityBytesPerSecond = 1000000, TransferTicks = 1 }, ticks, true) { }
        protected override bool SendImplementation(INetworkPacket packet)
        {
            OnSend?.Invoke();
            return true;
        }
    }

    [TestMethod]
    public void CoreConstructors_RejectNonpositiveTickValues()
    {
        foreach (var invalid in new[] { 0, -1 })
        {
            var ticks = Ticks();
            ticks.TickPeriod = TimeSpan.FromTicks(invalid);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new NetworkSimulator(
                new DijkstraPathFinder(), new NetworkLogger(false, TimeSpan.FromSeconds(1), new MonitoringIgnoreOption()), ticks, true));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Server(1, "s", "s", "test", "test", ticks, true));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Network(0, "n", "n", "eth",
                new NetworkOptions { CapacityBytesPerSecond = 1000, TransferTicks = 1 }, ticks, true));
            ticks.TickPeriod = TimeSpan.FromSeconds(1);
            ticks.OutgoingWaitTicks = invalid;
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => new Server(1, "s", "s", "test", "test", ticks, true));
        }
    }

    [TestMethod]
    public void Network_RejectsInvalidCapacityQueueWindowAndTransferTicks()
    {
        foreach (var field in new[] { "CapacityBytesPerSecond", "QueueCapacity", "ThroughputWindowTicks", "TransferTicks" })
        foreach (var invalid in new[] { 0, -1 })
        {
            var options = new NetworkOptions { CapacityBytesPerSecond = 1000, TransferTicks = 1 };
            var property = typeof(NetworkOptions).GetProperty(field)!;
            property.SetValue(options, Convert.ChangeType(invalid, property.PropertyType));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => Build(options), field);
        }
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity })
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => Build(
                new NetworkOptions { CapacityBytesPerSecond = invalid, TransferTicks = 1 }));
    }

    [TestMethod]
    public void Quiescence_FollowsPacketsThroughAllQueuesAndEmptyTopology()
    {
        var (graph, network, server) = Build();
        INetworkSimulator simulator = graph;
        Assert.IsTrue(simulator.IsQuiescent);
        server.Send(Packet(1));
        Assert.IsFalse(simulator.IsQuiescent);
        graph.Tick();
        server.Refresh();
        Assert.IsFalse(simulator.IsQuiescent);
        network.Refresh();
        Assert.IsFalse(simulator.IsQuiescent);
        graph.Tick();
        server.Refresh();
        Assert.IsTrue(simulator.IsQuiescent);
        graph.Clear();
        Assert.AreEqual(0, graph.Networks.Count);
        Assert.IsTrue(simulator.IsQuiescent);
    }

    [TestMethod]
    public void Network_EnforcesByteCapacityOverConfiguredRollingWindow()
    {
        var options = System.Text.Json.JsonSerializer.Deserialize<NetworkOptions>(
            "{\"CapacityBytesPerSecond\":10,\"TransferTicks\":1,\"ThroughputWindowTicks\":2}")!;
        var (graph, network, _) = Build(options);
        Assert.IsTrue(network.Send(Packet(1, 10)));
        Assert.IsTrue(network.Send(Packet(2, 10)));
        Assert.IsFalse(network.Send(Packet(3, 1)));
        graph.Tick();
        Assert.IsFalse(network.Send(Packet(4, 1)));
        graph.Tick();
        Assert.IsTrue(network.Send(Packet(5, 20)));
        Assert.IsFalse(network.Send(Packet(6, 1)));
    }

    [TestMethod]
    public void Network_RejectsAtConfiguredQueueCapacity()
    {
        var options = System.Text.Json.JsonSerializer.Deserialize<NetworkOptions>(
            "{\"Speed\":1000000,\"TransferTicks\":1,\"QueueCapacity\":2}")!;
        var (_, network, _) = Build(options);
        Assert.IsTrue(network.Send(Packet(1)));
        Assert.IsTrue(network.Send(Packet(2)));
        Assert.IsFalse(network.Send(Packet(3)));
        Assert.AreEqual(2L, network.QueueSize);
        network.Refresh();
        Assert.IsTrue(network.Send(Packet(4)));
    }

    [TestMethod]
    public void Refresh_KeepsRealAndVirtualCounterTimesDistinct()
    {
        var (graph, _, _) = Build();
        Assert.IsTrue(graph.Refresh(false, 0));
        var virtualCounter = graph.Counters.Counters.Single(c => c.Name == "VirtualTime");
        var realCounter = graph.Counters.Counters.Single(c => c.Name == "RealTime");
        Assert.AreEqual(TimeSpan.FromSeconds(1), ((EntityFX.MqttY.Counter.ValueCounter<TimeSpan>)virtualCounter).Value);
        Assert.IsTrue(((EntityFX.MqttY.Counter.ValueCounter<TimeSpan>)realCounter).Value <= graph.RealTime);
        Assert.AreNotEqual(((EntityFX.MqttY.Counter.ValueCounter<TimeSpan>)virtualCounter).Value,
            ((EntityFX.MqttY.Counter.ValueCounter<TimeSpan>)realCounter).Value);
    }

    [TestMethod]
    public void NodeIncoming_DeliversExactPacketsInFifoOrder()
    {
        var (graph, _, server) = Build();
        var packets = new[] { Packet(1), Packet(2), Packet(3) };
        var received = new List<INetworkPacket>();
        server.PacketReceived += (_, p) => received.Add(p);
        foreach (var packet in packets) server.Receive(packet);
        graph.Tick();
        server.Refresh();
        server.Refresh();
        CollectionAssert.AreEqual(packets, received);
        for (var i = 0; i < packets.Length; i++) Assert.AreSame(packets[i], received[i]);
    }

    [TestMethod]
    public void NodeOutgoing_DeliversExactPacketsInFifoOrder()
    {
        var (graph, network, server) = Build();
        var packets = new[] { Packet(1), Packet(2), Packet(3) };
        var received = new List<INetworkPacket>();
        server.PacketReceived += (_, p) => received.Add(p);
        foreach (var packet in packets) server.Send(packet);
        graph.Tick();
        server.Refresh();
        network.Refresh();
        graph.Tick();
        server.Refresh();
        network.Refresh();
        server.Refresh();
        CollectionAssert.AreEqual(packets, received);
        for (var i = 0; i < packets.Length; i++) Assert.AreSame(packets[i], received[i]);
    }

    [TestMethod]
    public void Network_DeliversExactPacketsInFifoOrder()
    {
        var (graph, network, server) = Build();
        var packets = new[] { Packet(1), Packet(2), Packet(3) };
        var received = new List<INetworkPacket>();
        server.PacketReceived += (_, p) => received.Add(p);
        foreach (var packet in packets) network.Send(packet);
        network.Refresh();
        graph.Tick();
        server.Refresh();
        network.Refresh();
        server.Refresh();
        CollectionAssert.AreEqual(packets, received);
        for (var i = 0; i < packets.Length; i++) Assert.AreSame(packets[i], received[i]);
    }
}
