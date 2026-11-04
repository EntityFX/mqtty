using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
namespace EntityFX.MqttY.Federation;

public sealed class TransmissionLedger
{
    private readonly FederationDeliveryLedger delivery;
    private readonly ConcurrentQueue<TransmissionResult> records = new();
    public TransmissionLedger(FederationDeliveryLedger delivery) => this.delivery = delivery;
    public void Observe(long tick, string client, int site, int target, string topic, byte[] payload, bool accepted)
    {
        var p = delivery.Find(payload) ?? throw new InvalidDataException("An outgoing payload differs from its source.");
        var role = client.StartsWith("publisher", StringComparison.Ordinal) ? "source" :
            topic.StartsWith("federation/routing/", StringComparison.Ordinal) ? "forward" : "localDelivery";
        records.Enqueue(new(tick, client, site, target, p.Id, p.Measurement, role, topic, payload.Length,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            checked(Encoding.UTF8.GetByteCount(topic) + payload.Length), accepted));
    }
    // Same-tick callbacks can finish in any thread order. Export a total order,
    // retaining every record, including duplicate attempts and rejected sends.
    public TransmissionResult[] Snapshot() => records.ToArray()
        .OrderBy(r => r.Tick).ThenBy(r => r.Client, StringComparer.Ordinal)
        .ThenBy(r => r.Site).ThenBy(r => r.Target).ThenBy(r => r.PublicationId)
        .ThenBy(r => r.Measurement).ThenBy(r => r.Role, StringComparer.Ordinal)
        .ThenBy(r => r.Topic, StringComparer.Ordinal).ThenBy(r => r.PayloadBytes)
        .ThenBy(r => r.PayloadSha256, StringComparer.Ordinal)
        .ThenBy(r => r.TopicPayloadBytes).ThenBy(r => r.Accepted).ToArray();
}
