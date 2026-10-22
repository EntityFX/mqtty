using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Utils;
namespace EntityFX.MqttY.Federation;

public sealed class FederationRunner
{
    public FederationSummary Run(FederationInput input, string? output = null)
    {
        if (output != null) FederationResultWriter.Claim(output);
        var o = input.Options; var ledger = new FederationDeliveryLedger(o); var traffic = new TransmissionLedger(ledger);
        FederationTopology? topology = null; var phases = new List<FederationPhase>(); var phase = "construct"; long phaseStart = 0;
        long scheduled = 0, rejected = 0, uniqueBefore = 0, completeBefore = 0;
        var measured = false; var measurementStarted = false; var status = "failed"; string? error = null; string? graphMl = null;
        TransitNetworkSnapshot? transitBaseline = null;
        try
        {
            topology = FederationTopologyBuilder.Build(input, ledger, traffic);
            graphMl = new SimpleGraphMlGenerator().SerializeNetworkGraph(topology.Graph);
            Finish(); Begin("connect");
            foreach (var c in topology.Connections) c.Client.BeginConnect(topology.Brokers[c.Target].Name, true);
            Until(() => topology.Graph.Clients.Values.All(c => c.IsConnected)); Finish();
            Begin("subscribe");
            foreach (var subscription in topology.Subscriptions) subscription.Client.BeginSubscribe(subscription.Filter, (MqttQos)o.Qos);
            foreach (var relay in topology.Relays) relay.SubscribeAll();
            Until(() => topology.Subscriptions.All(s => s.Client.IsSubscribed(s.Filter)) && topology.Relays.All(r =>
                r.Options!.ListenTopics.All(e => r.HasListenSubscription(e.Key, e.Value.Server, e.Value.Topics[0])))); Finish();
            Begin("warmup"); Drive(o.ToTicks(o.WarmupSeconds), false); Finish();
            // Finish the warmup cohort before resetting broker counters; ledger still rejects late warmup IDs.
            Begin("warmupDrain"); Until(() => topology.IsIdle); Finish();
            if (o.Topology == "star") transitBaseline = TransitNetworkSnapshot.Capture(topology);
            topology.Graph.ResetMeasurement();
            measurementStarted = true;
            Begin("measure"); Drive(o.ToTicks(o.MeasurementSeconds), true); measured = true;
            uniqueBefore = ledger.Unique; completeBefore = Sources().Sum(s => s.Metrics.Completed); Finish();
            Begin("drain"); Until(() => topology.IsIdle); Finish(); status = "completed";

            void Begin(string name) { phase = name; phaseStart = topology.Graph.TotalTicks; }
            void Finish() => phases.Add(new(phase, phaseStart, topology.Graph.TotalTicks, "completed"));
            void Refresh()
            {
                topology.Pump();
                if (!topology.Graph.Refresh(false, 0) || topology.Graph.Errors != 0) throw new InvalidOperationException("Simulator refresh failed.");
            }
            void Until(Func<bool> condition)
            {
                for (long i = 0, n = o.ToTicks(o.DrainTimeoutSeconds); !condition() && i < n; i++) Refresh();
                if (!condition()) throw new TimeoutException(phase + " timed out; partial evidence retained.");
            }
            void Drive(long count, bool measurement)
            {
                var perTick = o.OfferedRps * o.TickPeriod.TotalSeconds;
                var credit = new long[o.Publishers]; var cap = checked((long)Math.Ceiling(perTick / o.Publishers) + 1);
                long planned = 0; var next = 0;
                for (long t = 0; t < count; t++)
                {
                    var due = (long)Math.Floor((t + 1) * perTick + 1e-9); var slots = due - planned; planned = due;
                    if (measurement) scheduled += slots;
                    for (var p = 0; p < o.Publishers; p++)
                    {
                        credit[p] = Math.Min(cap, credit[p] + slots / o.Publishers + ((p - next + o.Publishers) % o.Publishers < slots % o.Publishers ? 1 : 0));
                        while (credit[p] > 0 && !topology.Publishers[p].HasPendingPublish)
                        {
                            var publication = ledger.Create(p, topology.Graph.TotalTicks, measurement);
                            var topic = o.Mode == FederationMode.Single ? FederationTopics.Delivered(publication.Origin) : FederationTopics.Input(publication.Origin);
                            if (!topology.Publishers[p].Publish(topic, publication.Payload, (MqttQos)o.Qos) && measurement) rejected++;
                            credit[p]--;
                        }
                    }
                    next = (int)((next + slots % o.Publishers) % o.Publishers); Refresh();
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.ToString(); phases.Add(new(phase, phaseStart, topology?.Graph.TotalTicks ?? 0, "failed"));
        }
        try
        {
            var sources = Sources(); var receivers = ledger.Snapshot(); var transmissions = traffic.Snapshot();
            var forwards = transmissions.Where(t => t.Measurement && t.Role == "forward").ToArray();
            var ph = FederationStatistics.Merge(sources.Select(s => s.Metrics.LatencyHistogramTicks));
            var rh = FederationStatistics.Merge(receivers.Select(r => r.LatencyHistogramTicks));
            var completed = sources.Sum(s => s.Metrics.Completed);
            double? Rate(long value) => measured ? value / o.MeasurementSeconds : null;
            double? P(double q) => o.Qos == 0 ? null : FederationStatistics.Quantile(ph, q, o.TickPeriod.TotalMilliseconds);
            double? R(double q) => FederationStatistics.Quantile(rh, q, o.TickPeriod.TotalMilliseconds);
            var summary = new FederationSummary(status, error, o.MeasurementSeconds, o.TickPeriod.TotalMilliseconds, scheduled,
                ledger.Attempted, rejected, scheduled - ledger.Attempted, completed, completeBefore, completed - completeBefore, Rate(completed),
                ledger.Expected, ledger.Unique, uniqueBefore, ledger.Unique - uniqueBefore, receivers.Length == 0 ? null : ledger.FullyDelivered,
                ledger.Expected > 0 ? 1d - (double)ledger.Unique / ledger.Expected : null, Rate(ledger.Unique),
                receivers.Sum(r => r.Duplicates), ledger.UnexpectedDeliveries, ledger.UnknownDeliveries,
                forwards.Length, forwards.LongCount(t => t.Accepted), forwards.LongCount(t => !t.Accepted), forwards.Sum(t => (long)t.TopicPayloadBytes),
                topology?.Federators.Sum(f => f.SuppressedDuplicates) ?? 0, topology?.Federators.Sum(f => f.QueueDrops) ?? 0,
                topology?.Federators.Sum(f => f.HopLimitDrops) ?? 0, topology?.Federators.Sum(f => f.CacheEntries) ?? 0,
                P(.5), P(.95), P(.99), R(.5), R(.95), R(.99), sources, receivers, ph, rh, phases.ToArray());
            if (output != null) FederationResultWriter.Write(output, input, summary, ledger.Publications(), ledger.Receipts(), transmissions, graphMl,
                o.Topology == "star" && topology != null ? TransitNetworkEvidence.Capture(o, topology, transitBaseline) : null);
            return summary;
        }
        finally { topology?.Dispose(); }

        SourcePublishResult[] Sources() => topology == null || !measurementStarted ? Array.Empty<SourcePublishResult>() :
            topology.Publishers.Select((p, i) =>
            {
                var broker = o.BrokerForSite(o.PublisherSite(i));
                var metrics = ((IPublisherMeasurementSource)topology.Brokers[broker]).GetPublisherMetrics(p.Name, (MqttQos)o.Qos);
                return new SourcePublishResult(i, o.PublisherSite(i), broker, metrics with { Rps = metrics.Completed / o.MeasurementSeconds });
            }).ToArray();
    }
}
