using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using System.Threading;

namespace EntityFX.MqttY.Federation;

public sealed class StaticFederator
{
    private readonly int site;
    private readonly FederationOptions options;
    private readonly FederationDeliveryLedger ledger;
    private readonly IForwardingPolicy policy;
    private readonly Dictionary<int, ObservedMqttClient> outputs;
    private readonly KeyValuePair<int, ObservedMqttClient>[] targets;
    private readonly Dictionary<int, Queue<QueuedPublication>> queues;
    private readonly Queue<QueuedPublication>[] targetQueues;
    private readonly HashSet<(int Origin, long Sequence)> seen = new();
    public long SuppressedDuplicates { get; private set; }
    public long QueueDrops { get; private set; }
    public long HopLimitDrops { get; private set; }
    public long CacheEntries => seen.Count;

    private int _activeCount;

    // A federator is idle only when it has no queued publications *and* no
    // publication was handed to an output client that is still waiting for the
    // graph to flush it. The counter replaces the O(targets) queue scan, but
    // the pending check must stay: Pump() decrements the counter as soon as a
    // message leaves the queue, before the client's publish is delivered.
    public bool IsIdle => Volatile.Read(ref _activeCount) == 0 &&
        outputs.Values.All(c => !c.HasPendingPublish);

    public FederatorTargetSnapshot[] GetQueueSnapshots() => targets
        .Select(p => new FederatorTargetSnapshot(site, p.Key, queues[p.Key].Count, p.Value.Name, p.Value.HasPendingPublish)).ToArray();

    public StaticFederator(int site, FederationOptions options, FederationDeliveryLedger ledger,
        IForwardingPolicy policy, Dictionary<int, ObservedMqttClient> outputs)
    {
        this.site = site; this.options = options; this.ledger = ledger; this.policy = policy; this.outputs = outputs;
        queues = outputs.Keys.ToDictionary(n => n, _ => new Queue<QueuedPublication>());
        targets = outputs.OrderBy(p => p.Key).Select(p => new KeyValuePair<int, ObservedMqttClient>(p.Key, p.Value)).ToArray();
        targetQueues = targets.Select(p => queues[p.Key]).ToArray();
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
        Interlocked.Increment(ref _activeCount);
    }

    public void Pump()
    {
        var published = 0;
        for (var i = 0; i < targets.Length; i++)
        {
            var queue = targetQueues[i];
            if (queue.Count == 0)
            {
                continue;
            }

            var target = targets[i];
            if (target.Value.HasPendingPublish || !queue.TryDequeue(out var p))
            {
                continue;
            }

            target.Value.Publish(p.Topic, p.Payload, (MqttQos)options.Qos);
            published++;
        }

        // One atomic update per federator per tick instead of one per message.
        // IsIdle is only polled after Pump returns, so a slightly delayed
        // decrement cannot hide queued work from the drain loop.
        if (published != 0)
        {
            Interlocked.Add(ref _activeCount, -published);
        }
    }
}
