using System.Buffers.Binary;
using System.Collections.ObjectModel;

namespace EntityFX.MqttY.LoadBalancing;

public sealed class DeliveryLedger
{
    private readonly int payloadBytes;
    private readonly double tickMilliseconds;
    private readonly Dictionary<long, DeliveryPublication> publications = new();
    private readonly HashSet<long> delivered = new();
    private readonly SortedDictionary<int, DeliveryBackendState> backends = new();
    private readonly SortedDictionary<long, long> latencyHistogramTicks = new();
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

        var id = checked(++nextId);
        var payload = new byte[payloadBytes];
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(0, 8), id);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8, 8), tick);
        publications.Add(id, new DeliveryPublication(publisher, backend, measurement, tick));
        var state = State(backend);
        if (measurement)
        {
            measurementCreated++;
            state.MeasurementCreated++;
        }
        return payload;
    }

    public void Receive(int backend, byte[] payload, long tick)
    {
        if (backend < 0) throw new ArgumentOutOfRangeException(nameof(backend));
        ArgumentNullException.ThrowIfNull(payload);
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        if (payload.Length != payloadBytes ||
            !publications.TryGetValue(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8)), out var publication) ||
            BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(8, 8)) != publication.Tick)
        {
            unknownDeliveries++;
            return;
        }
        if (tick < publication.Tick)
            throw new InvalidDataException("A message cannot arrive before its creation tick.");
        if (!publication.Measurement)
        {
            warmupDeliveries++;
            return;
        }

        var state = State(backend);
        if (backend != publication.Backend)
        {
            unexpectedDeliveries++;
            state.UnexpectedDeliveries++;
            return;
        }
        var id = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8));
        if (!delivered.Add(id))
        {
            duplicateDeliveries++;
            state.DuplicateDeliveries++;
            return;
        }

        uniqueDelivered++;
        state.UniqueDelivered++;
        var elapsedTicks = tick - publication.Tick;
        Increment(latencyHistogramTicks, elapsedTicks);
        Increment(state.LatencyHistogramTicks, elapsedTicks);
    }

    public DeliveryLedgerSnapshot Snapshot() => new(
        measurementCreated, uniqueDelivered, duplicateDeliveries, unexpectedDeliveries,
        warmupDeliveries, unknownDeliveries,
        Array.AsReadOnly(backends.Select(pair => new BackendDeliverySnapshot(
            pair.Key, pair.Value.MeasurementCreated, pair.Value.UniqueDelivered,
            pair.Value.DuplicateDeliveries, pair.Value.UnexpectedDeliveries,
            Freeze(pair.Value.LatencyHistogramTicks))).ToArray()),
        Freeze(latencyHistogramTicks), tickMilliseconds);

    private DeliveryBackendState State(int backend)
    {
        if (!backends.TryGetValue(backend, out var state))
        {
            state = new DeliveryBackendState();
            backends.Add(backend, state);
        }
        return state;
    }

    private static void Increment(IDictionary<long, long> histogram, long ticks) =>
        histogram[ticks] = histogram.TryGetValue(ticks, out var count) ? count + 1 : 1;

    private static IReadOnlyDictionary<long, long> Freeze(SortedDictionary<long, long> histogram) =>
        new ReadOnlyDictionary<long, long>(new SortedDictionary<long, long>(histogram));
}
