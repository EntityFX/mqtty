using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Optional, allocation-only-when-enabled packet trace used by the
/// characterization suite (S0). It records the logical send/receive points of a
/// run so a refactor can be compared packet-for-packet with the old behaviour.
/// The trace is written as sorted NDJSON; ordering inside a tick is normalized
/// so parallel scheduling noise does not make the file unreadable.
/// </summary>
public static class PacketTrace
{
    private sealed record TraceEvent(
        long Tick,
        string Kind,
        long Id,
        string From,
        string FromType,
        string To,
        string ToType,
        string Protocol,
        long? RequestId,
        int Ttl,
        int Bytes,
        string PayloadHash);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static ConcurrentQueue<TraceEvent>? _events;
    private static int _enabled;
    private static string? _outputPath;

    public static bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    /// <summary>Starts a trace run; the file is written by <see cref="Stop"/>.</summary>
    public static void Start(string outputPath)
    {
        _events = new ConcurrentQueue<TraceEvent>();
        _outputPath = outputPath;
        Volatile.Write(ref _enabled, 1);
    }

    /// <summary>Stops the trace and writes the sorted NDJSON file.</summary>
    public static void Stop()
    {
        Volatile.Write(ref _enabled, 0);
        var events = Interlocked.Exchange(ref _events, null);
        var path = Interlocked.Exchange(ref _outputPath, null);
        if (events == null || path == null)
        {
            return;
        }

        var ordered = events.ToArray()
            .OrderBy(e => e.Tick)
            .ThenBy(e => e.Kind, StringComparer.Ordinal)
            .ThenBy(e => e.Id)
            .ThenBy(e => e.From, StringComparer.Ordinal)
            .ThenBy(e => e.FromType, StringComparer.Ordinal)
            .ThenBy(e => e.To, StringComparer.Ordinal)
            .ThenBy(e => e.ToType, StringComparer.Ordinal)
            .ThenBy(e => e.RequestId)
            .ThenBy(e => e.PayloadHash, StringComparer.Ordinal);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var item in ordered)
        {
            writer.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
        }
    }

    public static void RecordSend(long tick, INetworkPacket packet) => Record(tick, "send", packet);

    public static void RecordReceive(long tick, INetworkPacket packet) => Record(tick, "receive", packet);

    private static void Record(long tick, string kind, INetworkPacket packet)
    {
        if (!IsEnabled)
        {
            return;
        }

        var payload = packet.Payload ?? Array.Empty<byte>();
        var hash = payload.Length == 0 ? string.Empty : Convert.ToHexString(SHA256.HashData(payload));
        var item = new TraceEvent(
            tick,
            kind,
            packet.Id,
            packet.From ?? string.Empty,
            packet.FromType.ToString(),
            packet.To ?? string.Empty,
            packet.ToType.ToString(),
            packet.Protocol ?? string.Empty,
            packet.RequestId,
            packet.Ttl,
            payload.Length,
            hash);
        _events?.Enqueue(item);
    }
}
