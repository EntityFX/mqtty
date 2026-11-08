using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class NodePacketSlotTests
{
    private static INetworkPacket Dummy() => new NetworkPacket<int>(
        1, null, 0, "from", "to", NodeType.Server, NodeType.Server,
        0, 0, new byte[] { 1 }, "test", 0, 1);

    [TestMethod]
    public void WaitCounter_AdvancesOnRefreshBoundary()
    {
        var slot = new NodePacketSlot(10, Dummy(), passTillNextTick: true, waitMode: false, waitTicks: 2, id: 1);

        Assert.AreEqual(10, slot.CreatedTick);
        Assert.AreEqual(2, slot.WaitTicks);
        Assert.IsFalse(slot.Released);
    }

    [TestMethod]
    public void Release_SignalsWaitModeEvent()
    {
        var slot = new NodePacketSlot(0, Dummy(), passTillNextTick: true, waitMode: true, waitTicks: 1, id: 1);
        Assert.IsFalse(slot.WaitIsReleased(TimeSpan.Zero));

        slot.Release();

        Assert.IsTrue(slot.Released);
        Assert.IsTrue(slot.WaitIsReleased(TimeSpan.Zero));
    }
}
