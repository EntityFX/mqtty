using EntityFX.MqttY.Contracts.Mqtt;
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
    public void PendingNamespaces_SeparateInboundAndOutboundPacketIds()
    {
        var session = new ClientSession("session", "session");
        var inbound = CreatePending("inbound", MqttQos.ExactlyOnce, 7,
            PendingMessageStatus.AwaitingPublishRelease);
        var outbound = CreatePending("outbound", MqttQos.AtLeastOnce, 7,
            PendingMessageStatus.PendingToAcknowledge);

        Assert.IsTrue(session.TryStartInboundPublish(inbound, duplicated: false));
        session.AddOutboundPending(outbound);

        Assert.IsTrue(session.TryAcknowledgeOutbound(7, out var acknowledged));
        Assert.AreSame(outbound, acknowledged, "PUBACK must consume the outbound flow.");
        Assert.IsTrue(session.TryGetInbound(7, out var retained));
        Assert.AreSame(inbound, retained, "The inbound QoS2 record must survive PUBACK.");
    }

    [TestMethod]
    public void DuplicateInboundPublish_BeforeReleaseKeepsTheOriginalRecord()
    {
        var session = new ClientSession("session", "session");
        var first = CreatePending("first", MqttQos.ExactlyOnce, 9,
            PendingMessageStatus.AwaitingPublishRelease);
        var duplicate = CreatePending("duplicate", MqttQos.ExactlyOnce, 9,
            PendingMessageStatus.AwaitingPublishRelease);

        Assert.IsTrue(session.TryStartInboundPublish(first, duplicated: false));
        Assert.IsFalse(session.TryStartInboundPublish(duplicate, duplicated: true));
        Assert.IsFalse(session.TryStartInboundPublish(duplicate, duplicated: false));

        var pending = session.GetPendingMessages().ToArray();
        Assert.AreEqual(1, pending.Length);
        Assert.AreSame(first, pending[0]);
    }

    [TestMethod]
    public void CompletedInboundMarker_SuppressesDupPublishAndReopensOnNonDup()
    {
        var session = new ClientSession("session", "session");
        var first = CreatePending("first", MqttQos.ExactlyOnce, 11,
            PendingMessageStatus.AwaitingPublishRelease);
        Assert.IsTrue(session.TryStartInboundPublish(first, duplicated: false));
        Assert.IsTrue(session.TryCompleteInboundPublish(11, 100, out var completed));
        Assert.AreSame(first, completed);
        Assert.IsTrue(session.HasCompletedInbound(11));

        Assert.IsFalse(session.TryStartInboundPublish(
            CreatePending("dup", MqttQos.ExactlyOnce, 11, PendingMessageStatus.AwaitingPublishRelease),
            duplicated: true));
        Assert.IsTrue(session.HasCompletedInbound(11),
            "A DUP publish must not drop the completed marker.");

        var reused = CreatePending("reused", MqttQos.ExactlyOnce, 11,
            PendingMessageStatus.AwaitingPublishRelease);
        Assert.IsTrue(session.TryStartInboundPublish(reused, duplicated: false));
        Assert.IsFalse(session.HasCompletedInbound(11),
            "A non-DUP publish may reuse the packet id and opens a new flow.");
        Assert.IsTrue(session.TryGetInbound(11, out var active));
        Assert.AreSame(reused, active);
    }

    [TestMethod]
    public void OutboundQos2_RequiresPubRecThenPubComp()
    {
        var session = new ClientSession("session", "session");
        var outbound = CreatePending("outbound", MqttQos.ExactlyOnce, 42,
            PendingMessageStatus.AwaitingPublishReceived);
        session.AddOutboundPending(outbound);

        Assert.IsFalse(session.TryCompleteOutboundQos2(42, out _),
            "PUBCOMP before PUBREC must not complete the flow.");
        Assert.IsTrue(session.TryAdvanceOutboundQos2(42, out var advanced));
        Assert.AreSame(outbound, advanced);
        Assert.AreEqual(PendingMessageStatus.AwaitingPublishComplete, outbound.Status);
        Assert.IsTrue(session.TryAdvanceOutboundQos2(42, out _),
            "A repeated PUBREC must still be answered with PUBREL.");

        Assert.IsTrue(session.TryCompleteOutboundQos2(42, out var done));
        Assert.AreSame(outbound, done);
        Assert.IsFalse(session.HasPendingMessages());
    }

    [TestMethod]
    public void PubAck_DoesNotConsumeOutboundQos2Flow()
    {
        var session = new ClientSession("session", "session");
        session.AddOutboundPending(CreatePending("outbound", MqttQos.ExactlyOnce, 5,
            PendingMessageStatus.AwaitingPublishReceived));

        Assert.IsFalse(session.TryAcknowledgeOutbound(5, out _));
        Assert.IsTrue(session.HasPendingMessages());
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

    private static PendingMessage CreatePending(string topic, MqttQos qos, ushort packetId,
        PendingMessageStatus status) =>
        PendingMessage.FromPublish(
            new PublishPacket(topic, qos, retain: false, duplicated: false, packetId: packetId)
            {
                Payload = new byte[] { 1, 2, 3 }
            }, status);
}
