using System.Security.Cryptography;
using EntityFX.MqttY.Contracts.Mqtt.Diagnostics;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.MqttY.Plugin.Mqtt.Diagnostics;

internal static class MqttPendingSnapshotBuilder
{
    public static MqttPendingSnapshot Capture(IEnumerable<ClientSession> source, int processingQueue,
        int detailLimit, int sessionLimit)
    {
        if (detailLimit is < 0 or > 1024 || sessionLimit is < 0 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(detailLimit), "Diagnostic detail limits must be between 0 and 1024.");
        var sessions = source.OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var messages = sessions.SelectMany(s => s.GetPendingMessages().Select(p => (Session: s.Id, Item: p)));
        var acks = sessions.SelectMany(s => s.GetPendingAcknowledgements().Select(p => (Session: s.Id, Item: p)));
        var states = messages.GroupBy(p => p.Item.Status.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
        var types = acks.GroupBy(p => p.Item.Type.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count());
        var messageCount = states.Values.Sum(); var ackCount = types.Values.Sum();
        var messageDetails = messages.Take(detailLimit).Select(p => new MqttPendingMessageSnapshot(p.Session,
            p.Item.Status.ToString(), (int)p.Item.QualityOfService, p.Item.PacketId, p.Item.CorrelationId,
            p.Item.Topic, p.Item.Payload.Length, Convert.ToHexString(SHA256.HashData(p.Item.Payload)).ToLowerInvariant())).ToArray();
        var ackDetails = acks.Take(Math.Max(0, detailLimit - messageDetails.Length))
            .Select(p => new MqttPendingAcknowledgementSnapshot(p.Session, p.Item.Type.ToString(), p.Item.PacketId)).ToArray();
        var sessionDetails = sessions.Take(sessionLimit).Select(s => new MqttSessionPendingSnapshot(s.Id,
            s.GetPendingMessages().Count(), s.GetPendingAcknowledgements().Count(),
            s.GetPendingMessages().Where(p => p.PacketId.HasValue).GroupBy(p => p.PacketId)
                .Count(g => g.Count() > 1))).ToArray();
        return new(sessions.Length, messageCount, ackCount, processingQueue, states, types,
            sessionDetails, messageDetails, ackDetails,
            sessionDetails.Length < sessions.Length || messageDetails.Length < messageCount || ackDetails.Length < ackCount);
    }
}
