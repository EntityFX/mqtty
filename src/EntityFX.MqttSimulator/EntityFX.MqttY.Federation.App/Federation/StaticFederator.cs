using EntityFX.MqttY.Contracts.Mqtt;
namespace EntityFX.MqttY.Federation;

public sealed class StaticFederator
{
    private readonly int site;
    private readonly FederationOptions options;
    private readonly FederationDeliveryLedger ledger;
    private readonly IForwardingPolicy policy;
    private readonly Dictionary<int, ObservedMqttClient> outputs;
    private readonly Dictionary<int, Queue<QueuedPublication>> queues;
    private readonly HashSet<(int Origin, long Sequence)> seen = new();
    public long SuppressedDuplicates { get; private set; }
    public long QueueDrops { get; private set; }
    public long HopLimitDrops { get; private set; }
    public long CacheEntries => seen.Count;
    public bool IsIdle => queues.Values.All(q => q.Count == 0) && outputs.Values.All(c => !c.HasPendingPublish);
    public FederatorTargetSnapshot[] GetQueueSnapshots() => outputs.OrderBy(p => p.Key)
        .Select(p => new FederatorTargetSnapshot(site, p.Key, queues[p.Key].Count, p.Value.Name, p.Value.HasPendingPublish)).ToArray();
    public StaticFederator(int site, FederationOptions options, FederationDeliveryLedger ledger,
        IForwardingPolicy policy, Dictionary<int, ObservedMqttClient> outputs)
    {
        this.site = site; this.options = options; this.ledger = ledger; this.policy = policy; this.outputs = outputs;
        queues = outputs.Keys.ToDictionary(n => n, _ => new Queue<QueuedPublication>());
    }
    public void Receive(MqttMessage message)
    {
        var p = ledger.Find(message.Payload) ?? throw new InvalidDataException("Unknown or changed federation payload.");
        var origin = p.Origin; var sequence = p.Sequence; var sender = -1; var remaining = options.HopLimit ?? options.Brokers;
        if (message.Topic.StartsWith("federation/input/", StringComparison.Ordinal))
        {
            if (message.Topic != FederationTopics.Input(site) || origin != site) throw new InvalidDataException("Wrong local source.");
        }
        else
        {
            var envelope = FederationTopics.Parse(message.Topic);
            if (envelope.Origin != origin || envelope.Sequence != sequence || !outputs.ContainsKey(envelope.Sender) || envelope.Sender == site)
                throw new InvalidDataException("Envelope/source mismatch.");
            sender = envelope.Sender; remaining = envelope.Remaining;
        }
        if (!seen.Add((origin, sequence))) { if (p.Measurement) SuppressedDuplicates++; return; }
        if (seen.Count > options.CacheCapacity) throw new InvalidOperationException("Federation cache exhausted; evidence retained.");
        if (options.Receivers().Contains(site)) Enqueue(site, FederationTopics.Delivered(origin), p);
        foreach (var next in policy.Destinations(site, origin, sender))
        {
            if (remaining == 0) { if (p.Measurement) HopLimitDrops++; continue; }
            Enqueue(next, FederationTopics.Routing(origin, sequence, site, remaining - 1), p);
        }
    }
    private void Enqueue(int target, string topic, FederationPublication p)
    {
        var queue = queues[target];
        if (queue.Count >= options.QueueCapacity) { if (p.Measurement) QueueDrops++; return; }
        queue.Enqueue(new(topic, p.Payload));
    }
    public void Pump()
    {
        foreach (var pair in outputs.OrderBy(p => p.Key))
            if (!pair.Value.HasPendingPublish && queues[pair.Key].TryDequeue(out var p))
                pair.Value.Publish(p.Topic, p.Payload, (MqttQos)options.Qos);
    }
}
