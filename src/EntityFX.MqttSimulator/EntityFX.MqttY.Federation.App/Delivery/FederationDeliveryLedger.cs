using System.Buffers.Binary;
using System.Security.Cryptography;
namespace EntityFX.MqttY.Federation;

public sealed class FederationDeliveryLedger
{
    private readonly FederationOptions options;
    private readonly Dictionary<long, FederationPublication> publications = new();
    private readonly Dictionary<int, long> sequences = new();
    private readonly Dictionary<int, HashSet<long>> received;
    private readonly Dictionary<int, SortedDictionary<long, long>> histograms;
    private readonly Dictionary<int, long> duplicates;
    private readonly List<DeliveryReceipt> receipts = new();
    public long UnknownDeliveries { get; private set; }
    public long WarmupDeliveries { get; private set; }
    public long UnexpectedDeliveries { get; private set; }
    public long Attempted => publications.Values.LongCount(p => p.Measurement);
    public long Expected => checked(Attempted * received.Count);
    public long Unique => received.Values.Sum(r => (long)r.Count);
    public long FullyDelivered => received.Count == 0 ? 0 : publications.Values.LongCount(p => p.Measurement && received.Values.All(r => r.Contains(p.Id)));
    public FederationDeliveryLedger(FederationOptions options)
    {
        this.options = options;
        received = options.Receivers().ToDictionary(s => s, _ => new HashSet<long>());
        histograms = options.Receivers().ToDictionary(s => s, _ => new SortedDictionary<long, long>());
        duplicates = options.Receivers().ToDictionary(s => s, _ => 0L);
    }
    public FederationPublication Create(int publisher, long tick, bool measurement)
    {
        var id = checked((long)publications.Count + 1);
        var payload = new byte[options.PayloadBytes];
        BinaryPrimitives.WriteInt64LittleEndian(payload, id);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8), tick);
        var origin = options.PublisherSite(publisher);
        var sequence = sequences.GetValueOrDefault(origin) + 1; sequences[origin] = sequence;
        var p = new FederationPublication(id, origin, sequence, publisher, tick, measurement, payload);
        publications.Add(id, p); return p;
    }
    public FederationPublication? Find(byte[] payload)
    {
        if (payload.Length != options.PayloadBytes) return null;
        return publications.TryGetValue(BinaryPrimitives.ReadInt64LittleEndian(payload), out var p) && payload.AsSpan().SequenceEqual(p.Payload) ? p : null;
    }
    public void Receive(int receiver, string topic, byte[] payload, long tick)
    {
        receipts.Add(new(receiver, payload.Length < 8 ? 0 : BinaryPrimitives.ReadInt64LittleEndian(payload), topic, tick,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()));
        var p = Find(payload);
        if (p == null) { UnknownDeliveries++; return; }
        if (!p.Measurement) { WarmupDeliveries++; return; }
        if (!received.ContainsKey(receiver) || topic != FederationTopics.Delivered(p.Origin)) { UnexpectedDeliveries++; return; }
        if (tick < p.Tick) throw new InvalidDataException("Delivery precedes creation.");
        if (!received[receiver].Add(p.Id)) { duplicates[receiver]++; return; }
        var h = histograms[receiver]; var elapsed = tick - p.Tick;
        h[elapsed] = h.GetValueOrDefault(elapsed) + 1;
    }
    public ReceiverResult[] Snapshot() => received.Select(kv => new ReceiverResult(kv.Key, kv.Value.Count, duplicates[kv.Key],
        new SortedDictionary<long, long>(histograms[kv.Key]))).ToArray();
    public FederationPublication[] Publications() => publications.Values.ToArray();
    public DeliveryReceipt[] Receipts() => receipts.ToArray();
}
