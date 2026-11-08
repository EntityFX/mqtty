using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace EntityFX.MqttY.Federation;

/// <summary>
/// Federation delivery accounting. Delivery callbacks run on refresh workers,
/// so publications are read-mostly, duplicate detection is protected per
/// receiver (one receiver node per site), and receipts are appended concurrently.
/// </summary>
public sealed class FederationDeliveryLedger
{
    private readonly FederationOptions options;
    private readonly ConcurrentDictionary<long, FederationPublication> publications = new();
    private readonly ConcurrentDictionary<int, long> sequences = new();
    private readonly ConcurrentDictionary<int, ReceiverState> receivers;
    private readonly ConcurrentQueue<DeliveryReceipt> receipts = new();
    private long nextId;
    private long unknownDeliveries;
    private long warmupDeliveries;
    private long unexpectedDeliveries;

    public long UnknownDeliveries => Interlocked.Read(ref unknownDeliveries);
    public long WarmupDeliveries => Interlocked.Read(ref warmupDeliveries);
    public long UnexpectedDeliveries => Interlocked.Read(ref unexpectedDeliveries);

    public long Attempted => publications.Values.LongCount(p => p.Measurement);

    public long Expected => checked(Attempted * receivers.Count);

    public long Unique
    {
        get
        {
            long unique = 0;
            foreach (var state in receivers.Values)
            {
                lock (state.Sync)
                {
                    unique += state.Received.Count;
                }
            }
            return unique;
        }
    }

    public long FullyDelivered
    {
        get
        {
            if (receivers.IsEmpty) return 0;
            return publications.Values.LongCount(publication =>
                publication.Measurement && receivers.Values.All(state =>
                {
                    lock (state.Sync)
                    {
                        return state.Received.Contains(publication.Id);
                    }
                }));
        }
    }

    public FederationDeliveryLedger(FederationOptions options)
    {
        this.options = options;
        receivers = new ConcurrentDictionary<int, ReceiverState>(
            options.Receivers().Select(site => new KeyValuePair<int, ReceiverState>(site, new ReceiverState())));
    }

    public FederationPublication Create(int publisher, long tick, bool measurement)
    {
        var id = Interlocked.Increment(ref nextId);
        var payload = new byte[options.PayloadBytes];
        BinaryPrimitives.WriteInt64LittleEndian(payload, id);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8), tick);
        var origin = options.PublisherSite(publisher);
        var sequence = sequences.AddOrUpdate(origin, 1, static (_, value) => value + 1);
        // The payload is forwarded many times; hash it once here instead of on
        // every transmission/delivery bookkeeping record.
        var payloadSha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var publication = new FederationPublication(id, origin, sequence, publisher, tick, measurement, payload,
            payloadSha256);
        publications[id] = publication;
        return publication;
    }

    public FederationPublication? Find(byte[] payload)
    {
        if (payload.Length != options.PayloadBytes)
        {
            return null;
        }

        return publications.TryGetValue(BinaryPrimitives.ReadInt64LittleEndian(payload), out var publication) &&
               payload.AsSpan().SequenceEqual(publication.Payload)
            ? publication
            : null;
    }

    public void Receive(int receiver, string topic, byte[] payload, long tick)
    {
        var publication = Find(payload);
        if (publication == null)
        {
            // Unknown payloads still need evidence; known ones reuse the hash
            // computed when the publication was created.
            var unknownSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            receipts.Enqueue(new(receiver,
                payload.Length < 8 ? 0 : BinaryPrimitives.ReadInt64LittleEndian(payload),
                topic, tick, unknownSha));
            Interlocked.Increment(ref unknownDeliveries);
            return;
        }

        receipts.Enqueue(new(receiver, publication.Id, topic, tick, publication.PayloadSha256));
        if (!publication.Measurement)
        {
            Interlocked.Increment(ref warmupDeliveries);
            return;
        }
        if (!receivers.TryGetValue(receiver, out var state) || topic != FederationTopics.Delivered(publication.Origin))
        {
            Interlocked.Increment(ref unexpectedDeliveries);
            return;
        }
        if (tick < publication.Tick)
        {
            throw new InvalidDataException("Delivery precedes creation.");
        }

        lock (state.Sync)
        {
            if (!state.Received.Add(publication.Id))
            {
                state.Duplicates++;
                return;
            }

            var elapsed = tick - publication.Tick;
            state.Histogram[elapsed] = state.Histogram.GetValueOrDefault(elapsed) + 1;
        }
    }

    public ReceiverResult[] Snapshot() => receivers.OrderBy(pair => pair.Key)
        .Select(pair =>
        {
            lock (pair.Value.Sync)
            {
                return new ReceiverResult(pair.Key, pair.Value.Received.Count, pair.Value.Duplicates,
                    new SortedDictionary<long, long>(pair.Value.Histogram));
            }
        })
        .ToArray();

    public FederationPublication[] Publications() => publications.Values
        .OrderBy(publication => publication.Origin)
        .ThenBy(publication => publication.Sequence)
        .ThenBy(publication => publication.Id)
        .ToArray();

    public DeliveryReceipt[] Receipts() => receipts
        .OrderBy(r => r.Tick).ThenBy(r => r.Receiver)
        .ThenBy(r => r.PublicationId).ThenBy(r => r.Topic, StringComparer.Ordinal)
        .ThenBy(r => r.PayloadSha256, StringComparer.Ordinal).ToArray();

    private sealed class ReceiverState
    {
        public object Sync { get; } = new();
        public HashSet<long> Received { get; } = new();
        public SortedDictionary<long, long> Histogram { get; } = new();
        public long Duplicates;
    }
}
