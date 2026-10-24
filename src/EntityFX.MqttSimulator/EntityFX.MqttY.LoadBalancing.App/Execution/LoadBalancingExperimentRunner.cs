using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Utils;

namespace EntityFX.MqttY.LoadBalancing;

public sealed class LoadBalancingExperimentRunner
{
    public ExperimentSummary Run(PreparedExperiment input, string output,
        CancellationToken cancellationToken = default, Action<INetworkSimulator, string>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        return RunCore(input, output, cancellationToken, onProgress);
    }

    public ExperimentSummary RunInMemory(PreparedExperiment input, CancellationToken cancellationToken = default,
        Action<INetworkSimulator, string>? onProgress = null) => RunCore(input, null, cancellationToken, onProgress);

    private ExperimentSummary RunCore(PreparedExperiment input, string? output, CancellationToken cancellationToken,
        Action<INetworkSimulator, string>? onProgress)
    {
        // Claim a new directory before simulation; previous evidence is never overwritten.
        if (output != null) ExperimentResultWriter.CreateOutput(output);
        var options = input.Options;
        var ledger = new DeliveryLedger(options.PayloadBytes, options.TickPeriod.TotalMilliseconds);
        LoadBalancingTopology? topology = null;
        var phases = new List<PhaseResult>();
        var status = "failed";
        string? error = null;
        long start = 0, end = 0, attempts = 0, rejected = 0, scheduled = 0;
        int pendingBefore = 0;
        long phaseStart = 0;
        var phase = "construct";
        var beforeDrain = new Dictionary<int, BrokerQosMetricsSnapshot>();
        var deliveryBefore = ledger.Snapshot();
        string? graphMl = null, plantUml = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            topology = LoadBalancingTopologyBuilder.Build(input);
            var graph = topology.Graph;
            if (output != null)
            {
                graphMl = new SimpleGraphMlGenerator().SerializeNetworkGraph(graph);
                plantUml = new PlantUmlGraphGenerator().Generate(graph);
            }
            CompletePhase();
            for (var i = 0; i < topology.Receivers.Length; i++)
            {
                var backend = i;
                topology.Receivers[i].MessageReceived += (_, message) => ledger.Receive(backend, message.Payload, graph.TotalTicks);
                topology.Receivers[i].BeginConnect(topology.Brokers[i].Name, cleanSession: true);
            }
            for (var i = 0; i < topology.Publishers.Length; i++)
                topology.Publishers[i].BeginConnect(topology.Brokers[input.Assignments[i]].Name, cleanSession: true);
            BeginPhase("connect");
            Until(() => graph.Clients.Values.All(c => c.IsConnected));
            CompletePhase();
            BeginPhase("subscribe");
            foreach (var receiver in topology.Receivers)
                receiver.BeginSubscribe(LoadBalancingTopologyBuilder.Topic, (MqttQos)options.SubscribeQos);
            Until(() => topology.Receivers.All(c => c.IsSubscribed(LoadBalancingTopologyBuilder.Topic)));
            CompletePhase();
            BeginPhase("warmup");
            Drive(options.ToTicks(options.WarmupSeconds), false);
            CompletePhase();
            graph.ResetMeasurement();
            start = graph.TotalTicks;
            BeginPhase("measure");
            Drive(options.ToTicks(options.MeasurementSeconds), true);
            end = graph.TotalTicks;
            deliveryBefore = ledger.Snapshot();
            pendingBefore = topology.Publishers.Count(c => c.HasPendingPublish);
            for (var i = 0; i < topology.Brokers.Length; i++)
                beforeDrain[i] = topology.Brokers[i].GetMetrics().ByQos[(MqttQos)options.PublishQos];
            CompletePhase();
            BeginPhase("drain");
            Until(() => graph.IsQuiescent);
            CompletePhase();
            status = "completed";

            void BeginPhase(string name) { phase = name; phaseStart = graph.TotalTicks; }
            void CompletePhase() => phases.Add(new(phase, phaseStart, graph.TotalTicks, "completed"));
            void Refresh()
            {
                cancellationToken.ThrowIfCancellationRequested();
                // RefreshWithCounters resets the graph on error; retain partial evidence instead.
                if (!graph.Refresh(false, 0) || graph.Errors != 0)
                    throw new InvalidOperationException("Simulator refresh failed; graph errors=" + graph.Errors);
                onProgress?.Invoke(graph, phase);
            }
            void Until(Func<bool> condition)
            {
                var limit = options.ToTicks(options.DrainTimeoutSeconds);
                for (long tick = 0; !condition() && tick < limit; tick++) Refresh();
                if (!condition()) throw new TimeoutException($"{phase} timed out after {limit} ticks.");
            }
            void Drive(long ticks, bool measurement)
            {
                var perTick = options.OfferedRps * options.TickPeriod.TotalSeconds;
                var credit = new long[topology.Publishers.Length];
                var creditCap = checked((long)Math.Ceiling(perTick / topology.Publishers.Length) + 1);
                long scheduledSoFar = 0;
                var nextScheduledPublisher = 0;
                for (long tick = 0; tick < ticks; tick++)
                {
                    // Compute cumulative schedule to avoid repeatedly rounding away fractional intensity.
                    var due = checked((long)Math.Floor((tick + 1) * perTick + 1e-9));
                    var slots = due - scheduledSoFar;
                    if (measurement) scheduled += slots;
                    scheduledSoFar = due;
                    var each = slots / topology.Publishers.Length;
                    var extra = (int)(slots % topology.Publishers.Length);
                    for (var publisher = 0; publisher < topology.Publishers.Length; publisher++)
                    {
                        var offset = (publisher - nextScheduledPublisher + topology.Publishers.Length) % topology.Publishers.Length;
                        credit[publisher] = Math.Min(creditCap, credit[publisher] + each + (offset < extra ? 1 : 0));
                        // A busy publisher never transfers its assigned load to a free publisher.
                        while (credit[publisher] > 0 && !topology.Publishers[publisher].HasPendingPublish)
                        {
                            var payload = ledger.Create(publisher, input.Assignments[publisher], measurement, graph.TotalTicks);
                            var accepted = topology.Publishers[publisher].Publish(LoadBalancingTopologyBuilder.Topic, payload, (MqttQos)options.PublishQos);
                            if (measurement) { attempts++; if (!accepted) rejected++; }
                            credit[publisher]--;
                        }
                    }
                    nextScheduledPublisher = (nextScheduledPublisher + extra) % topology.Publishers.Length;
                    Refresh();
                }
            }
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            phases.Add(new(phase, phaseStart, topology?.Graph.TotalTicks ?? 0, "failed"));
        }

        try
        {
            var delivery = ledger.Snapshot();
            var backends = topology?.Brokers.Select((broker, i) => new BackendResult(i,
                input.Assignments.Count(n => n == i), Normalize(broker.GetMetrics().ByQos[(MqttQos)options.PublishQos]),
                beforeDrain.TryGetValue(i, out var before) ? before.Completed : 0,
                delivery.Backends.FirstOrDefault(b => b.Backend == i)?.UniqueDelivered ?? 0,
                deliveryBefore.Backends.FirstOrDefault(b => b.Backend == i)?.UniqueDelivered ?? 0)).ToArray() ?? Array.Empty<BackendResult>();
            var histogram = MergeHistograms(backends.Select(b => b.Metrics.LatencyHistogramTicks));
            var completeMeasurement = end > start;
            var completed = backends.Sum(b => b.Metrics.Completed);
            var backendAttempted = backends.Sum(b => b.Metrics.Attempted);
            var expected = backends.Sum(b => b.Metrics.ExpectedDeliveries);
            double? PublishQuantile(double q) => options.PublishQos == 0 ? null : Quantile(histogram, q, options.TickPeriod.TotalMilliseconds);
            double? ReceiverQuantile(double q) => Quantile(delivery.LatencyHistogramTicks, q, options.TickPeriod.TotalMilliseconds);
            var summary = new ExperimentSummary(status, error, options.OfferedRps, scheduled, attempts, rejected,
                scheduled - attempts, pendingBefore, topology?.Publishers.Count(c => c.HasPendingPublish) ?? 0,
                start, end, topology?.Graph.TotalTicks ?? 0, topology?.Graph.Errors ?? 0, options.MeasurementSeconds,
                options.TickPeriod.TotalMilliseconds,
                completeMeasurement ? completed / options.MeasurementSeconds : null,
                completeMeasurement ? delivery.UniqueDelivered / options.MeasurementSeconds : null,
                attempts > 0 ? 1d - (double)delivery.UniqueDelivered / attempts : null,
                backendAttempted > 0 ? (double)backends.Sum(b => b.Metrics.RateRejected + b.Metrics.PublishFailed) / backendAttempted : null,
                expected > 0 ? 1d - (double)backends.Sum(b => b.Metrics.Delivered) / expected : null,
                PublishQuantile(.5), PublishQuantile(.95), PublishQuantile(.99),
                ReceiverQuantile(.5), ReceiverQuantile(.95), ReceiverQuantile(.99), deliveryBefore.UniqueDelivered,
                delivery.UniqueDelivered - deliveryBefore.UniqueDelivered, delivery, backends, histogram, phases);
            if (output != null) ExperimentResultWriter.Write(output, input, summary, graphMl, plantUml);
            return summary;

            BrokerQosMetricsSnapshot Normalize(BrokerQosMetricsSnapshot metrics) =>
                metrics with { Rps = end > start ? metrics.Completed / options.MeasurementSeconds : 0 };
        }
        finally { topology?.Graph.Clear(); }
    }

    public static SortedDictionary<long, long> MergeHistograms(IEnumerable<IReadOnlyDictionary<long, long>> histograms)
    {
        var merged = new SortedDictionary<long, long>();
        foreach (var histogram in histograms)
            foreach (var (tick, count) in histogram)
                merged[tick] = merged.GetValueOrDefault(tick) + count;
        return merged;
    }

    public static double? Quantile(IReadOnlyDictionary<long, long> histogram, double q, double tickMilliseconds)
    {
        var total = histogram.Values.Sum();
        if (total == 0) return null;
        var rank = Math.Max(1, (long)Math.Ceiling(q * total));
        long cumulative = 0;
        foreach (var (tick, count) in histogram.OrderBy(p => p.Key))
            if ((cumulative += count) >= rank) return tick * tickMilliseconds;
        throw new InvalidDataException("Invalid histogram.");
    }
}
