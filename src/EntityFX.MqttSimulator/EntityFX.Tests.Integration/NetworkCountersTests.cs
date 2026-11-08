using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class NetworkCountersTests
{
    private static INetworkPacket Packet(int bytes) => new NetworkPacket<int>(
        1, null, 0, "from", "to", NodeType.Server, NodeType.Server,
        0, 0, new byte[bytes], "test", HeaderBytes: 0, OutgoingTicks: 1);

    [TestMethod]
    public void OutboundThroughputUsesOutboundWindow()
    {
        CounterClock.Publish(0, 0);
        var counters = new NetworkCounters("net", "N",
            new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(1), CounterHistoryDepth = 10 },
            enabled: true, historyEnabled: false, throughputWindowTicks: 1);

        counters.CountInbound(Packet(100));
        CounterClock.Publish(100, 0);
        counters.CountOutbound(Packet(50));

        counters.Refresh(110, 0);

        var outbound = counters.Counters.Single(counter => counter.Name == "OutboundThroughput")
            as ValueCounter<double>;
        Assert.IsNotNull(outbound);
        // 50 bytes over a 10-tick window at 1 ms/tick => 5000 B/s.
        Assert.AreEqual(5000d, outbound!.Value, 1e-6);
    }
}
