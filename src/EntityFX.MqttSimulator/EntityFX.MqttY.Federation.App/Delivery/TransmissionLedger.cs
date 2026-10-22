using System.Text;
using System.Security.Cryptography;
namespace EntityFX.MqttY.Federation;

public sealed class TransmissionLedger
{
    private readonly FederationDeliveryLedger delivery;
    private readonly List<TransmissionResult> records = new();
    public TransmissionLedger(FederationDeliveryLedger delivery) => this.delivery = delivery;
    public void Observe(long tick, string client, int site, int target, string topic, byte[] payload, bool accepted)
    {
        var p = delivery.Find(payload) ?? throw new InvalidDataException("An outgoing payload differs from its source.");
        var role = client.StartsWith("publisher", StringComparison.Ordinal) ? "source" :
            topic.StartsWith("federation/routing/", StringComparison.Ordinal) ? "forward" : "localDelivery";
        records.Add(new(tick, client, site, target, p.Id, p.Measurement, role, topic, payload.Length,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            checked(Encoding.UTF8.GetByteCount(topic) + payload.Length), accepted));
    }
    public TransmissionResult[] Snapshot() => records.ToArray();
}
