using System.Buffers.Binary;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Experiments.Tracing;

/// <summary>Opt-in raw observations of a single model broker, including warmup IDs.</summary>
internal sealed class PublicationCohortTrace : IDisposable
{
    private readonly INetworkSimulator graph;
    private readonly double secondsPerTick;
    private readonly int bytes;
    private readonly StreamWriter publishes;
    private readonly StreamWriter deliveries;
    private readonly StreamWriter connections;
    private readonly StreamWriter subscriptions;
    private readonly string directory;
    private readonly Dictionary<(int Publisher, bool Measurement), long> sequences = new();
    private readonly List<MqttClient> clients = new();
    private readonly List<MqttReceiver> receivers = new();
    private MqttClient[] observedClients = Array.Empty<MqttClient>();
    private readonly MqttQos qos;
    private readonly Dictionary<string, bool> connectionStates = new();
    private readonly Dictionary<string, bool> subscriptionStates = new();
    private readonly HashSet<string> subscriberNames = new();
    private readonly Dictionary<string, string[]> subscriberTopics = new();
    private readonly Dictionary<string, MqttQos> subscriberQos = new();

    public PublicationCohortTrace(string directory, INetworkSimulator graph, double secondsPerTick, int bytes, MqttQos qos)
    {
        this.directory = directory;
        this.graph = graph;
        this.secondsPerTick = secondsPerTick;
        this.bytes = bytes;
        this.qos = qos;
        publishes = Open("publishes.ndjson");
        deliveries = Open("deliveries.ndjson");
        connections = Open("connections.ndjson");
        subscriptions = Open("subscriptions.ndjson");
    }

    private StreamWriter Open(string name) => new(new FileStream(Path.Combine(directory, name), FileMode.CreateNew));
    private static void Write<T>(StreamWriter writer, T value) => writer.WriteLine(JsonSerializer.Serialize(value));

    public void Attach(IEnumerable<MqttClient> publishers, IEnumerable<MqttReceiver> subscribers)
    {
        foreach (var client in publishers)
        {
            clients.Add(client);
            client.PublicationOutcome += OnOutcome;
        }
        foreach (var receiver in subscribers)
        {
            receivers.Add(receiver);
            subscriberNames.Add(receiver.Name + receiver.Options!.Server);
            subscriberTopics[receiver.Name + receiver.Options!.Server] = receiver.Options.Topics;
            subscriberQos[receiver.Name + receiver.Options!.Server] = receiver.Options.Qos;
            receiver.MessageReceived += OnDelivery;
        }
        observedClients = graph.Clients.Values.OfType<MqttClient>().ToArray();
        RecordConnections("connectStarted");
    }

    public byte[] CreateAndRecord(int publisher, bool measurement)
    {
        var sequence = sequences.GetValueOrDefault((publisher, measurement));
        sequences[(publisher, measurement)] = sequence + 1;
        var payload = new byte[bytes];
        payload[0] = measurement ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), publisher);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(8, 8), sequence);
        Write(publishes, new { @event = "attempt", id = Id(payload), publisher, sequence,
            payloadBytes = bytes, elapsedSeconds = graph.TotalTicks * secondsPerTick });
        return payload;
    }

    private static string Id(byte[] payload) => Convert.ToHexString(payload.AsSpan(0, 16)).ToLowerInvariant();

    private void OnOutcome(object? sender, Diagnostics.MqttPublishOutcome value) =>
        Write(publishes, new { @event = value.Completed ? "completed" : "failed", id = Id(value.Payload),
            elapsedSeconds = value.EndedTick * secondsPerTick,
            latencyMs = value.Completed && qos != MqttQos.AtMostOnce
                ? (double?)((value.EndedTick - value.StartedTick) * secondsPerTick * 1000) : null,
            error = value.Reason });

    private void OnDelivery(object? sender, MqttMessage value) =>
        Write(deliveries, new { id = Id(value.Payload), elapsedSeconds = graph.TotalTicks * secondsPerTick,
            payloadBytes = value.Payload.Length, receiver = (sender as MqttReceiver)?.Name });

    public void RecordConnections(string phase)
    {
        foreach (var client in observedClients)
            WriteConnection(client, phase);
    }

    public void RecordConnectionChanges()
    {
        foreach (var client in observedClients)
        {
            if (!connectionStates.TryGetValue(client.Name, out var previous) || previous != client.IsConnected)
                WriteConnection(client, "stateChange");
            if (!subscriberNames.Contains(client.Name)) continue;
            foreach (var topic in subscriberTopics[client.Name])
            {
                var key = client.Name + "\0" + topic;
                var subscribed = client.IsSubscribed(topic);
                if (subscriptionStates.TryGetValue(key, out var previousSubscription) && previousSubscription == subscribed) continue;
                subscriptionStates[key] = subscribed;
                var granted = subscribed ? (int?)client.Subscribtions.Values.SelectMany(value => value)
                    .Single(value => value.TopicFilter == topic).MaximumQualityOfService : null;
                Write(subscriptions, new { client = client.Name, clientId = client.ClientId, role = "subscriber",
                    topic, state = subscribed ? "subscribed" : "unsubscribed",
                    requestedQos = (int)subscriberQos[client.Name], grantedQos = granted, elapsedSeconds = graph.TotalTicks * secondsPerTick });
            }
        }
    }

    private void WriteConnection(MqttClient client, string phase)
    {
        connectionStates[client.Name] = client.IsConnected;
        var role = clients.Contains(client) ? "publisher" :
            subscriberNames.Contains(client.Name) ? "subscriber" : "other";
        Write(connections, new { client = client.Name, clientId = client.ClientId, role,
            connected = client.IsConnected, state = client.IsConnected ? "connected" : "disconnected", phase,
            elapsedSeconds = graph.TotalTicks * secondsPerTick });
    }

    public void SavePhases(long start, long end, ExperimentRunStatus status)
    {
        RecordConnections("observationEnd");
        using var output = new FileStream(Path.Combine(directory, "phases.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(output, new { timeOrigin = "sharedMonotonic", qos = (int)qos,
            measurementStartSeconds = start * secondsPerTick, measurementEndSeconds = end * secondsPerTick,
            drainEndSeconds = graph.TotalTicks * secondsPerTick, completed = status == ExperimentRunStatus.Completed,
            connectionObservation = "everyModelTick",
            policy = "closedLoopPerPublisher; publication IDs; actual publisher ACK/failure" });
    }

    public void Dispose()
    {
        foreach (var client in clients) client.PublicationOutcome -= OnOutcome;
        foreach (var receiver in receivers) receiver.MessageReceived -= OnDelivery;
        publishes.Dispose(); deliveries.Dispose(); connections.Dispose(); subscriptions.Dispose();
    }
}
