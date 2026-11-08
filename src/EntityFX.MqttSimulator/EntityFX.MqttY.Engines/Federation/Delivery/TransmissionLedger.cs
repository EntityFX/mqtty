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
            p.PayloadSha256,
            checked(Encoding.UTF8.GetByteCount(topic) + payload.Length), accepted));
    }
    // Same-tick callbacks can finish in any thread order. Export a total order,
    // retaining every record, including duplicate attempts and rejected sends.
    // Array.Sort with one comparer avoids the LINQ OrderBy/ThenBy chain, which
    // dominates the post-drain summary for ~1M forwarding records.
    public TransmissionResult[] Snapshot()
    {
        var result = records.ToArray();
        Array.Sort(result, TransmissionOrder.Instance);
        return result;
    }

    private sealed class TransmissionOrder : IComparer<TransmissionResult>
    {
        public static readonly TransmissionOrder Instance = new();

        public int Compare(TransmissionResult a, TransmissionResult b)
        {
            var c = a.Tick.CompareTo(b.Tick);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Client, b.Client);
            if (c != 0) return c;
            c = a.Site.CompareTo(b.Site);
            if (c != 0) return c;
            c = a.Target.CompareTo(b.Target);
            if (c != 0) return c;
            c = a.PublicationId.CompareTo(b.PublicationId);
            if (c != 0) return c;
            c = a.Measurement.CompareTo(b.Measurement);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Role, b.Role);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Topic, b.Topic);
            if (c != 0) return c;
            c = a.PayloadBytes.CompareTo(b.PayloadBytes);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.PayloadSha256, b.PayloadSha256);
            if (c != 0) return c;
            c = a.TopicPayloadBytes.CompareTo(b.TopicPayloadBytes);
            return c != 0 ? c : a.Accepted.CompareTo(b.Accepted);
        }
    }
}
