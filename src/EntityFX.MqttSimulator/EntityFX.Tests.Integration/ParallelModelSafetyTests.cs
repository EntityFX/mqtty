using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Network;
using ModelNetwork = EntityFX.MqttY.Network.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Regression)]
public class ParallelModelSafetyTests
{
    private static (NetworkSimulator Graph, ModelNetwork[] Networks) Build(IPathFinder finder, int count)
    {
        var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1 };
        var graph = new NetworkSimulator(finder, new NullNetworkLogger(), ticks, true) { Construction = true };
        var networks = Enumerable.Range(0, count).Select(i => new ModelNetwork(i, $"n{i:D2}", $"n{i:D2}", "ethernet",
            new NetworkOptions { CapacityBytesPerSecond = 1000000, QueueCapacity = 1024, TransferTicks = 1 }, ticks, true)).ToArray();
        foreach (var network in networks) graph.AddNetwork(network);
        for (var i = 1; i < count; i++)
        {
            graph.Link(networks[i - 1].Name, networks[i].Name);
            graph.Link(networks[i].Name, networks[i - 1].Name);
        }
        graph.Construction = false;
        graph.UpdateRoutes();
        return (graph, networks);
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ColdRouteCache_ConcurrentMissesPreserveEveryRoute(bool indexed)
    {
        IPathFinder finder = indexed ? new DijkstraIndexPathFinder() : new DijkstraPathFinder();
        var (graph, networks) = Build(finder, 16);
        var pairs = (from source in networks from target in networks where source != target select (source, target)).ToArray();
        var expected = pairs.Select(p => finder.GetPath(p.source, p.target).Select(n => n.Name).ToArray()).ToArray();
        for (var generation = 0; generation < 3; generation++)
        {
            graph.UpdateRoutes();
            using var barrier = new Barrier(8);
            var workers = Enumerable.Range(0, 8).Select(worker => Task.Factory.StartNew(() =>
            {
                Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
                for (var index = worker; index < pairs.Length; index += 8)
                {
                    var pair = pairs[index];
                    CollectionAssert.AreEqual(expected[index], finder.GetPath(pair.source, pair.target).Select(n => n.Name).ToArray());
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Task.WaitAll(workers);
        }
    }

    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public void RouteRebuild_InvalidatesCachedRouteAndKeepsReturnedSnapshot(bool indexed)
    {
        IPathFinder finder = indexed ? new DijkstraIndexPathFinder() : new DijkstraPathFinder();
        var (graph, networks) = Build(finder, 3);
        var old = finder.GetPath(networks[0], networks[2]);
        CollectionAssert.AreEqual(new[] { "n01", "n02" }, old.Select(n => n.Name).ToArray());
        graph.Link("n00", "n02");
        graph.UpdateRoutes();
        CollectionAssert.AreEqual(new[] { "n02" }, finder.GetPath(networks[0], networks[2]).Select(n => n.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "n01", "n02" }, old.Select(n => n.Name).ToArray());
    }

    [TestMethod]
    public void FederationLedger_ConcurrentReceiversKeepEveryReceiptAndHistogramEntry()
    {
        var options = new FederationOptions { Brokers = 4, Publishers = 4, PayloadBytes = 16 };
        var ledger = new FederationDeliveryLedger(options);
        var publications = Enumerable.Range(0, 1000).Select(i => ledger.Create(i % 4, i, true)).ToArray();
        Parallel.For(0, publications.Length * 4, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var p = publications[i / 4];
            ledger.Receive(i % 4, FederationTopics.Delivered(p.Origin), p.Payload, p.Tick + 5);
        });
        Assert.AreEqual(4000L, ledger.Unique);
        Assert.AreEqual(4000, ledger.Receipts().Length);
        Assert.AreEqual(1000L, ledger.FullyDelivered);
        Assert.AreEqual(4000L, ledger.Snapshot().Sum(r => r.LatencyHistogramTicks.Values.Sum()));
    }

    [TestMethod]
    public void TransmissionLedger_ConcurrentPublishersRetainEveryEvent()
    {
        var delivery = new FederationDeliveryLedger(new FederationOptions { Brokers = 4, Publishers = 4, PayloadBytes = 16 });
        var ledger = new TransmissionLedger(delivery);
        var payloads = Enumerable.Range(0, 4000).Select(i => delivery.Create(i % 4, i, true).Payload).ToArray();
        Parallel.For(0, payloads.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            ledger.Observe(i + 1, "publisher" + i, i % 4, 0, "test", payloads[i], true));
        var events = ledger.Snapshot();
        Assert.AreEqual(4000, events.Length);
        Assert.AreEqual(4000, events.Select(e => e.PublicationId).Distinct().Count());
    }

    [TestMethod]
    public void BalancingLedger_ConcurrentBackendsKeepGlobalCountsAndHistogram()
    {
        var ledger = new DeliveryLedger(16, .1);
        var payloads = Enumerable.Range(0, 4000).Select(i => ledger.Create(i, i % 4, true, i)).ToArray();
        Parallel.For(0, payloads.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            ledger.Receive(i % 4, payloads[i], i + 5));
        var snapshot = ledger.Snapshot();
        Assert.AreEqual(4000L, snapshot.UniqueDelivered);
        Assert.AreEqual(4000L, snapshot.LatencyHistogramTicks.Values.Sum());
        Assert.AreEqual(4000L, snapshot.Backends.Sum(b => b.UniqueDelivered));
    }

    [TestMethod]
    public void EvidenceExport_IsIndependentOfCallbackOrderAndRetainsDuplicates()
    {
        (DeliveryReceipt[], TransmissionResult[]) Export(bool reverse)
        {
            var ledger = new FederationDeliveryLedger(new FederationOptions { Brokers = 4, Publishers = 4, PayloadBytes = 16 });
            var traffic = new TransmissionLedger(ledger);
            var publications = Enumerable.Range(0, 32).Select(i => ledger.Create(i % 4, i, true)).ToArray();
            var order = Enumerable.Range(0, 32).Append(0);
            foreach (var i in reverse ? order.Reverse() : order)
            {
                var p = publications[i];
                ledger.Receive(i % 4, FederationTopics.Delivered(p.Origin), p.Payload, 100);
                traffic.Observe(100, "publisher" + i % 4, i % 4, 0, "test", p.Payload, true);
            }
            return (ledger.Receipts(), traffic.Snapshot());
        }
        var forward = Export(false);
        var backward = Export(true);
        Assert.AreEqual(33, forward.Item1.Length);
        Assert.AreEqual(33, forward.Item2.Length);
        CollectionAssert.AreEqual(forward.Item1, backward.Item1);
        CollectionAssert.AreEqual(forward.Item2, backward.Item2);
    }
}
