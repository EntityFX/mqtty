using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class ClientSessionTests
{
    [TestMethod]
    public void PrunePendingAcknowledgements_ExpiresOldCompletedMarkersOnly()
    {
        var retention = StorageExtensions.CompletedAcknowledgementRetentionTicks;
        var session = new ClientSession("session", "session");

        session.AddPendingAcknowledgement(new PendingAcknowledgement
        {
            Type = MqttPacketType.PublishComplete,
            PacketId = 1,
            Tick = 0
        });
        session.AddPendingAcknowledgement(new PendingAcknowledgement
        {
            Type = MqttPacketType.PublishComplete,
            PacketId = 2,
            Tick = retention
        });
        session.AddPendingAcknowledgement(new PendingAcknowledgement
        {
            Type = MqttPacketType.PublishAck,
            PacketId = 3,
            Tick = 0
        });

        session.PrunePendingAcknowledgements(retention);

        var remaining = session.GetPendingAcknowledgements().ToArray();
        Assert.IsFalse(remaining.Any(ack => ack.Type == MqttPacketType.PublishComplete && ack.PacketId == 1),
            "Expired PublishComplete marker must be evicted.");
        Assert.IsTrue(remaining.Any(ack => ack.Type == MqttPacketType.PublishComplete && ack.PacketId == 2),
            "Fresh PublishComplete marker must be kept.");
        Assert.IsTrue(remaining.Any(ack => ack.Type == MqttPacketType.PublishAck && ack.PacketId == 3),
            "Non-PublishComplete acknowledgements must not be evicted.");
    }

    [TestMethod]
    public void PrunePendingAcknowledgements_EnforcesCompletedCapacity()
    {
        var capacity = StorageExtensions.MaxCompletedAcknowledgements;
        var session = new ClientSession("session", "session");
        for (var i = 0; i < capacity + 5; i++)
        {
            session.AddPendingAcknowledgement(new PendingAcknowledgement
            {
                Type = MqttPacketType.PublishComplete,
                PacketId = (ushort)i,
                Tick = 0
            });
        }

        session.PrunePendingAcknowledgements(0);

        Assert.AreEqual(capacity, session.GetPendingAcknowledgements().Count(),
            "Completed markers must be capped per session.");
    }
}
