using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace EntityFX.MqttY.LoadBalancing;

/// <summary>
/// Delivery accounting for the LB experiment. Receiver callbacks run on
/// refresh workers, so the ledger must not put all deliveries behind one lock:
/// publications are read-mostly, duplicate detection is an atomic add, and each
/// backend owns its counters/histogram (one receiver node per backend).
/// </summary>
public sealed class DeliveryLedger
{
    private readonly int payloadBytes;
    private readonly double tickMilliseconds;
    private readonly ConcurrentDictionary<long, DeliveryPublication> publications = new();
    private readonly ConcurrentDictionary<long, byte> delivered = new();
    private readonly ConcurrentDictionary<int, DeliveryBackendState> backends = new();
    private long nextId;
    private long measurementCreated;
    private long uniqueDelivered;
    private long duplicateDeliveries;
    private long unexpectedDeliveries;
    private long warmupDeliveries;
    private long unknownDeliveries;

    public DeliveryLedger(int payloadBytes, double tickMilliseconds)
    {
        if (payloadBytes < 16)
            throw new ArgumentOutOfRangeException(nameof(payloadBytes), "Payload must fit an eight-byte ID and tick.");
        if (!double.IsFinite(tickMilliseconds) || tickMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickMilliseconds));
        this.payloadBytes = payloadBytes;
        this.tickMilliseconds = tickMilliseconds;
    }

    public byte[] Create(int publisher, int backend, bool measurement, long tick)
    {
        if (publisher < 0) throw new ArgumentOutOfRangeException(nameof(publisher));
        if (backend < 0) throw new ArgumentOutOfRangeException(nameof(backend));
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        var id = Interlocked.Increment(ref nextId);
        var payload = new byte[payloadBytes];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), id);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8, 8), tick);
        publications[id] = new DeliveryPublication(publisher, backend, measurement, tick);
        var state = State(backend);
        if (measurement)
        {
            Interlocked.Increment(ref measurementCreated);
            Interlocked.Increment(ref state.MeasurementCreated);
        }
        return payload;
    }

    public void Receive(int backend, byte[] payload, long tick)
    {
        if (backend < 0) throw new ArgumentOutOfRangeException(nameof(backend));
        ArgumentNullException.ThrowIfNull(payload);
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        var id = payload.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8)) : 0;
        if (payload.Length != payloadBytes ||
            !publications.TryGetValue(id, out var publication) ||
            BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(8, 8)) != publication.Tick)
        {
            Interlocked.Increment(ref unknownDeliveries);
            return;
        }
        if (tick < publication.Tick)
            throw new InvalidDataException("A message cannot arrive before its creation tick.");
        if (!publication.Measurement)
        {
            Interlocked.Increment(ref warmupDeliveries);
            return;
        }

        var state = State(backend);
        if (backend != publication.Backend)
        {
            Interlocked.Increment(ref unexpectedDeliveries);
            Interlocked.Increment(ref state.UnexpectedDeliveries);
            return;
        }
        if (!delivered.TryAdd(id, 0))
        {
            Interlocked.Increment(ref duplicateDeliveries);
            Interlocked.Increment(ref state.DuplicateDeliveries);
            return;
        }

        Interlocked.Increment(ref uniqueDelivered);
        Interlocked.Increment(ref state.UniqueDelivered);
        var elapsedTicks = tick - publication.Tick;
        lock (state.Sync)
        {
            Increment(state.LatencyHistogramTicks, elapsedTicks);
        }
    }

    public DeliveryLedgerSnapshot Snapshot()
    {
        var backendSnapshots = backends.OrderBy(pair => pair.Key)
            .Select(pair => new BackendDeliverySnapshot(
                pair.Key, Interlocked.Read(ref pair.Value.MeasurementCreated),
                Interlocked.Read(ref pair.Value.UniqueDelivered),
                Interlocked.Read(ref pair.Value.DuplicateDeliveries),
                Interlocked.Read(ref pair.Value.UnexpectedDeliveries),
                FreezeBackend(pair.Value)))
            .ToArray();

        // The global histogram used to be updated under the lock; merge the
        // per-backend histograms once at snapshot time instead.
        var merged = new SortedDictionary<long, long>();
        foreach (var state in backends.Values)
        {
            lock (state.Sync)
            {
                foreach (var (elapsed, count) in state.LatencyHistogramTicks)
                {
                    merged[elapsed] = merged.GetValueOrDefault(elapsed) + count;
                }
            }
        }

        return new(
            Interlocked.Read(ref measurementCreated),
            Interlocked.Read(ref uniqueDelivered),
            Interlocked.Read(ref duplicateDeliveries),
            Interlocked.Read(ref unexpectedDeliveries),
            Interlocked.Read(ref warmupDeliveries),
            Interlocked.Read(ref unknownDeliveries),
            Array.AsReadOnly(backendSnapshots),
            Freeze(merged),
            tickMilliseconds);
    }

    private DeliveryBackendState State(int backend) =>
        backends.GetOrAdd(backend, static _ => new DeliveryBackendState());

    private static void Increment(IDictionary<long, long> histogram, long ticks) =>
        histogram[ticks] = histogram.TryGetValue(ticks, out var count) ? count + 1 : 1;

    private static IReadOnlyDictionary<long, long> Freeze(SortedDictionary<long, long> histogram) =>
        new ReadOnlyDictionary<long, long>(new SortedDictionary<long, long>(histogram));

    private static IReadOnlyDictionary<long, long> FreezeBackend(DeliveryBackendState state)
    {
        lock (state.Sync)
        {
            return Freeze(state.LatencyHistogramTicks);
        }
    }
}
