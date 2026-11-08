using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class NodeMonitoringPacketTests
{
    private static INetworkPacket Dummy() => new NetworkPacket<int>(
        1, null, 0, "from", "to", NodeType.Server, NodeType.Server,
        0, 0, new byte[] { 1 }, "test", 0, 1);

    [TestMethod]
    public void Reset_ClearsReleasedEventInWaitMode()
    {
        var packet = new NodeMonitoringPacket(0, Dummy(), passTillNextTick: true, waitMode: true);
        packet.Release();
        Assert.IsTrue(packet.WaitIsReleased(TimeSpan.Zero), "Release must signal the waiter.");

        packet.Reset(1, Dummy(), passTillNextTick: true, waitMode: true, waitTicks: 1, id: 2, sendTick: 1);

        Assert.IsFalse(packet.WaitIsReleased(TimeSpan.Zero),
            "A recycled packet must not inherit the previous release signal.");
    }
}
