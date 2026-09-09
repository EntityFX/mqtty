using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Utils;

namespace EntityFX.MqttY.Plugin.Mqtt.Experiments;

public sealed class MqttRelayExperimentRunner
{
    private readonly TicksOptions _ticks;
    private readonly NetworkOptions _network;
    private readonly IBrokerBenchmarkRepository _profiles;
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public MqttRelayExperimentRunner(
        TicksOptions ticks, NetworkOptions network,
        IBrokerBenchmarkRepository? profiles = null)
    {
        ticks.Validate();
        network.Validate();
        _ticks = ticks;
        _network = network;
        _profiles = profiles ?? new BrokerBenchmarkRepository();
    }

    public MqttRelayExperimentResult Run(
        MqttRelayExperimentOptions options, string outputRoot, ExperimentProvenance provenance)
    {
        var runId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        var runDirectory = Path.Combine(Path.GetFullPath(outputRoot), runId);
        Directory.CreateDirectory(runDirectory);
        var phases = new List<ExperimentPhaseBoundary>();
        var assignedTypes = Array.Empty<string?>();
        INetworkSimulator? graph = null;
        long measurementStart = 0;
        long measurementEnd = 0;
        ExperimentRunStatus status = ExperimentRunStatus.Failed;
        string? error = null;
        IReadOnlyDictionary<string, BrokerMetricsSnapshot> snapshots =
            new Dictionary<string, BrokerMetricsSnapshot>();
        var simulated = EmptyMetrics();
        ExperimentMetricSummary? reference = null;

        try
        {
            options.Validate();
            assignedTypes = options.ResolveBrokerTypes().ToArray();
            var assignedProfiles = ResolveProfiles(options, assignedTypes);

            var phaseStart = 0L;
            graph = BuildGraph(options, assignedProfiles);
            phases.Add(new ExperimentPhaseBoundary("construct", phaseStart, graph.TotalTicks, "completed"));

            phaseStart = graph.TotalTicks;
            var (relays, receivers, publishers) = StartAndConnect(graph);
            if (!RefreshUntil(graph, () => graph.Clients.Values.All(client => client.IsConnected),
                    options.DrainTimeoutTicks, options))
                throw new ExperimentPhaseException(ExperimentRunStatus.ConnectionFailed,
                    "Connection barrier timed out.");
            phases.Add(new ExperimentPhaseBoundary("connect", phaseStart, graph.TotalTicks, "completed"));

            phaseStart = graph.TotalTicks;
            foreach (var relay in relays) relay.SubscribeAll();
            foreach (var receiver in receivers) receiver.SubscribeAll();
            if (!RefreshUntil(graph, () => AreSubscribed(relays, receivers),
                    options.DrainTimeoutTicks, options))
                throw new ExperimentPhaseException(ExperimentRunStatus.SubscriptionFailed,
                    "Subscription barrier timed out.");
            phases.Add(new ExperimentPhaseBoundary("subscribe", phaseStart, graph.TotalTicks, "completed"));

            var payload = CreatePayload(options.PayloadBytes, options.RandomSeed);
            phaseStart = graph.TotalTicks;
            DriveOfferedLoad(graph, publishers, options.WarmupTicks, payload, options);
            phases.Add(new ExperimentPhaseBoundary("warmup", phaseStart, graph.TotalTicks, "completed"));

            graph.ResetMeasurement();
            measurementStart = graph.TotalTicks;
            phases.Add(new ExperimentPhaseBoundary("resetMeasurement",
                measurementStart, measurementStart, "completed"));
            DriveOfferedLoad(graph, publishers, options.MeasurementTicks, payload, options);
            measurementEnd = graph.TotalTicks;
            phases.Add(new ExperimentPhaseBoundary("measure",
                measurementStart, measurementEnd, "completed"));

            phaseStart = graph.TotalTicks;
            if (!RefreshUntil(graph, () => graph.IsQuiescent,
                    options.DrainTimeoutTicks, options))
                throw new ExperimentPhaseException(ExperimentRunStatus.DrainTimedOut,
                    $"Drain did not reach quiescence within {options.DrainTimeoutTicks} ticks.");
            phases.Add(new ExperimentPhaseBoundary("drain", phaseStart, graph.TotalTicks, "completed"));

            snapshots = NormalizeSnapshots(graph, measurementStart, measurementEnd);
            simulated = AggregateSnapshots(snapshots);
            reference = BuildReference(options, assignedProfiles);
            status = ExperimentRunStatus.Completed;
        }
        catch (ExperimentPhaseException exception)
        {
            status = exception.Status;
            error = exception.Message;
            phases.Add(new ExperimentPhaseBoundary(StatusPhase(status),
                graph?.TotalTicks ?? 0, graph?.TotalTicks ?? 0, "failed"));
        }
        catch (Exception exception)
        {
            status = ExperimentRunStatus.Failed;
            error = exception.ToString();
        }

        if (graph != null && snapshots.Count == 0 && measurementEnd > measurementStart)
        {
            snapshots = NormalizeSnapshots(graph, measurementStart, measurementEnd);
            simulated = AggregateSnapshots(snapshots);
        }
        var criteria = Evaluate(options, reference, simulated);
        var summary = new MqttRelayExperimentSummary(status, measurementStart, measurementEnd,
            simulated, reference, criteria, error);
        Export(runDirectory, runId, options, provenance, assignedTypes, phases,
            graph, snapshots, summary);
        graph?.Clear();
        return new MqttRelayExperimentResult(runDirectory, summary);
    }

    private IReadOnlyList<MqttBrokerProfile?> ResolveProfiles(
        MqttRelayExperimentOptions options, IReadOnlyList<string?> assignedTypes)
    {
        return assignedTypes.Select(name =>
        {
            if (name == null) return null;
            var profile = _profiles.Get(name);
            profile.For(options.PayloadBytes, options.PublishQos, options.ResolveProfileClientCount());
            return profile;
        }).ToArray();
    }

    private INetworkSimulator BuildGraph(
        MqttRelayExperimentOptions options, IReadOnlyList<MqttBrokerProfile?> profiles)
    {
        var generic = new GenericBuilder();
        var evaluator = generic.GetMqttTopicEvaluator();
        var packetManager = generic.GetMqttPacketManager(evaluator);
        var graph = new NetworkSimulator(generic.GetPathFinder(), generic.GetNullNetworkLogger(),
            _ticks, options.EnableCounters);
        var clientBuilder = generic.GetClientBuilder((index, name, protocolType, specification,
            network, ticks, enableCounters, group, groupAmount, additional) =>
        {
            var client = new MqttClient(packetManager, index, name, $"mqtt://{name}",
                protocolType, specification, name.Replace(".", string.Empty), ticks, enableCounters)
            {
                Group = group,
                GroupAmount = groupAmount
            };
            network.AddClient(client);
            graph.AddClient(client);
            return client;
        });
        var builder = new MqttNetworkBuilder(graph, packetManager, evaluator, clientBuilder,
            ordinal => profiles[ordinal], options.RandomSeed, options.SubscribeQos,
            options.ProfileClientCountMode == ProfileClientCountMode.Publishers ? options.ClientsPerBroker : null);
        graph.Construction = true;
        builder.BuildSimpleTree(options.Brokers, options.NetworkLength,
            options.ClientsPerBroker, 1, null, true, _ticks, _network);
        if (options.TopologyMode == ExperimentTopologyMode.BrokerFidelity)
            builder.BuildBrokerFidelity(graph, _ticks);
        else
            builder.BuildMqttRelay(graph, _ticks);
        graph.Construction = false;
        graph.UpdateRoutes();
        return graph;
    }

    private static (MqttRelay[] Relays, MqttReceiver[] Receivers, MqttClient[] Publishers)
        StartAndConnect(INetworkSimulator graph)
    {
        var publishers = graph.Clients.Values.OfType<MqttClient>()
            .Where(client => client.Group == null).ToArray();
        var relays = graph.Applications.Values.OfType<MqttRelay>().ToArray();
        var receivers = graph.Applications.Values.OfType<MqttReceiver>().ToArray();
        foreach (var relay in relays) relay.Start();
        foreach (var receiver in receivers) receiver.Start();
        foreach (var publisher in publishers)
        {
            var broker = publisher.Network!.Servers.Values.Single();
            publisher.BeginConnect(broker.Name);
        }
        return (relays, receivers, publishers);
    }

    private static bool AreSubscribed(
        IEnumerable<MqttRelay> relays, IEnumerable<MqttReceiver> receivers)
    {
        return relays.All(relay => relay.Options!.ListenTopics.All(listen =>
                listen.Value.Topics.All(topic =>
                    relay.HasListenSubscription(listen.Key, listen.Value.Server, topic)))) &&
            receivers.All(receiver => receiver.Options!.Topics.All(topic =>
                receiver.HasListenSubscription(receiver.Options.Server, topic)));
    }

    private void DriveOfferedLoad(
        INetworkSimulator graph, IReadOnlyList<MqttClient> publishers,
        long ticks, byte[] payload, MqttRelayExperimentOptions options)
    {
        double tokens = 0;
        var tokensPerTick = options.OfferedRps * _ticks.TickPeriod.TotalSeconds;
        var publisherIndex = 0;
        for (long tick = 0; tick < ticks; tick++)
        {
            tokens += tokensPerTick;
            var publishCount = (long)Math.Floor(tokens + 1e-12);
            tokens -= publishCount;
            for (long message = 0; message < publishCount; message++)
            {
                var publisher = publishers[publisherIndex++ % publishers.Count];
                publisher.Publish("telemetry/data", payload, options.PublishQos);
            }
            graph.RefreshWithCounters(options.Parallel, options.RefreshStrategy);
        }
    }

    private static bool RefreshUntil(
        INetworkSimulator graph, Func<bool> condition, long timeoutTicks,
        MqttRelayExperimentOptions options)
    {
        for (long tick = 0; tick <= timeoutTicks; tick++)
        {
            if (condition()) return true;
            if (tick < timeoutTicks)
                graph.RefreshWithCounters(options.Parallel, options.RefreshStrategy);
        }
        return false;
    }

    private IReadOnlyDictionary<string, BrokerMetricsSnapshot> NormalizeSnapshots(
        INetworkSimulator graph, long measurementStart, long measurementEnd)
    {
        var seconds = Math.Max(0, measurementEnd - measurementStart) * _ticks.TickPeriod.TotalSeconds;
        return graph.Servers.Values.OfType<IMqttBroker>().ToDictionary(broker => broker.Name, broker =>
        {
            var source = broker.GetMetrics();
            var byQos = source.ByQos.ToDictionary(item => item.Key, item =>
            {
                var value = item.Value;
                return value with { Rps = seconds > 0 ? value.Completed / seconds : 0 };
            });
            return new BrokerMetricsSnapshot(measurementStart, measurementEnd,
                BrokerMetricsSnapshot.ReadOnly(byQos));
        });
    }

    private ExperimentMetricSummary AggregateSnapshots(
        IReadOnlyDictionary<string, BrokerMetricsSnapshot> snapshots)
    {
        var metrics = snapshots.Values.SelectMany(snapshot => snapshot.ByQos.Values).ToArray();
        if (metrics.Length == 0) return EmptyMetrics();
        var attempted = metrics.Sum(metric => metric.Attempted);
        var expected = metrics.Sum(metric => metric.ExpectedDeliveries);
        var histogram = metrics.SelectMany(metric => metric.LatencyHistogramTicks)
            .GroupBy(item => item.Key).OrderBy(group => group.Key)
            .Select(group => (Ticks: group.Key, Count: group.Sum(item => item.Value))).ToArray();
        var rank = (long)Math.Ceiling(histogram.Sum(item => item.Count) * 0.99);
        long cumulative = 0;
        long? p99Ticks = null;
        foreach (var item in histogram)
        {
            cumulative += item.Count;
            if (cumulative < rank) continue;
            p99Ticks = item.Ticks;
            break;
        }
        return new ExperimentMetricSummary(attempted,
            metrics.Sum(metric => metric.Admitted), metrics.Sum(metric => metric.Completed),
            expected, metrics.Sum(metric => metric.Delivered),
            metrics.Sum(metric => metric.RateRejected), metrics.Sum(metric => metric.PublishFailed),
            metrics.Sum(metric => metric.DeliveryDropped), metrics.Sum(metric => metric.Rps),
            attempted == 0 ? 0 : metrics.Sum(metric => metric.RateRejected + metric.PublishFailed) / (double)attempted,
            expected == 0 ? 0 : 1 - metrics.Sum(metric => metric.Delivered) / (double)expected,
            p99Ticks * _ticks.TickPeriod.TotalMilliseconds);
    }

    private static ExperimentMetricSummary? BuildReference(
        MqttRelayExperimentOptions options, IReadOnlyList<MqttBrokerProfile?> profiles)
    {
        if (profiles.All(profile => profile == null)) return null;
        var samples = profiles.Select(profile => profile!.For(
            options.PayloadBytes, options.PublishQos, options.ResolveProfileClientCount())).ToArray();
        double Throughput(CalibratedMqttQosSample sample) => sample.TargetCompletedRps ?? sample.CapacityRps;
        var capacity = samples.Sum(Throughput);
        var attempted = samples.Sum(sample => sample.AttemptedRps ?? sample.CapacityRps);
        double? p99 = options.PublishQos == MqttQos.AtMostOnce ? null :
            capacity == 0 ? null : samples.Sum(sample =>
                (sample.ObservedLatencyQuantiles ?? sample.ProcessingLatencyQuantiles)!.P99Ms * Throughput(sample)) / capacity;
        return new ExperimentMetricSummary(0, 0, 0, 0, 0, 0, 0, 0, capacity,
            samples.Sum(sample => (sample.TargetPublishFailureRate ?? sample.PublishFailureRate) *
                (sample.AttemptedRps ?? sample.CapacityRps)) / attempted,
            capacity == 0 ? 0 : samples.Sum(sample => sample.ConditionalDeliveryLossRate * Throughput(sample)) / capacity,
            p99);
    }

    private static IReadOnlyList<AcceptanceCriterionResult> Evaluate(
        MqttRelayExperimentOptions options, ExperimentMetricSummary? reference,
        ExperimentMetricSummary simulated)
    {
        if (reference == null)
            return new[]
            {
                new AcceptanceCriterionResult("rps", null, simulated.CompletedRps, null, 0.15, "notApplicable"),
                new AcceptanceCriterionResult("publishFailureRate", null, simulated.PublishFailureRate, null, 0.05, "notApplicable"),
                new AcceptanceCriterionResult("deliveryLossRate", null, simulated.DeliveryLossRate, null, 0.05, "notApplicable"),
                new AcceptanceCriterionResult("latencyP99Ms", null, simulated.LatencyP99Ms, null,
                    options.PublishQos == MqttQos.AtMostOnce ? null : 0.20, "notApplicable")
            };

        AcceptanceCriterionResult Relative(string name, double? expected, double? actual, double limit)
        {
            if (expected is null || actual is null || expected == 0)
                return new AcceptanceCriterionResult(name, expected, actual, null, limit, "notApplicable");
            var error = Math.Abs(actual.Value - expected.Value) / expected.Value;
            return new AcceptanceCriterionResult(name, expected, actual, error, limit,
                error <= limit ? "passed" : "failed");
        }
        AcceptanceCriterionResult Absolute(string name, double expected, double actual, double limit)
        {
            var error = Math.Abs(actual - expected);
            return new AcceptanceCriterionResult(name, expected, actual, error, limit,
                error <= limit ? "passed" : "failed");
        }
        return new[]
        {
            Relative("rps", reference.CompletedRps, simulated.CompletedRps, 0.15),
            Absolute("publishFailureRate", reference.PublishFailureRate, simulated.PublishFailureRate, 0.05),
            Absolute("deliveryLossRate", reference.DeliveryLossRate, simulated.DeliveryLossRate, 0.05),
            options.PublishQos == MqttQos.AtMostOnce
                ? new AcceptanceCriterionResult("latencyP99Ms", null, simulated.LatencyP99Ms,
                    null, null, "notApplicable")
                : Relative("latencyP99Ms", reference.LatencyP99Ms, simulated.LatencyP99Ms, 0.20)
        };
    }

    private void Export(
        string directory, string runId, MqttRelayExperimentOptions options,
        ExperimentProvenance provenance, IReadOnlyList<string?> assignedTypes,
        IReadOnlyList<ExperimentPhaseBoundary> phases, INetworkSimulator? graph,
        IReadOnlyDictionary<string, BrokerMetricsSnapshot> snapshots,
        MqttRelayExperimentSummary summary)
    {
        var manifest = new ExperimentManifest(runId, DateTimeOffset.UtcNow, options,
            assignedTypes, provenance, RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription, Environment.ProcessorCount,
            _ticks.TickPeriod, phases, summary.MeasurementStartTick, summary.MeasurementEndTick,
            graph?.Servers.Values.OfType<IMqttBroker>().Select(broker => new BrokerClientCount(
                broker.Name, broker.GetServerClients().Count(), options.ResolveProfileClientCount())).ToArray()
                ?? Array.Empty<BrokerClientCount>());
        WriteJsonNew(Path.Combine(directory, "experiment-manifest.json"), manifest);
        WriteJsonNew(Path.Combine(directory, "summary.json"), summary);
        WriteJsonNew(Path.Combine(directory, "counters.json"), new
        {
            brokers = snapshots,
            graph = graph?.Counters.Dump() ?? string.Empty
        });
        WriteJsonNew(Path.Combine(directory, "latency-histogram.json"),
            snapshots.ToDictionary(broker => broker.Key,
                broker => broker.Value.ByQos.ToDictionary(qos => qos.Key,
                    qos => qos.Value.LatencyHistogramTicks)));
        WriteTextNew(Path.Combine(directory, "topology.graphml"),
            graph == null ? string.Empty : new SimpleGraphMlGenerator().SerializeNetworkGraph(graph));
        WriteTextNew(Path.Combine(directory, "topology.puml"),
            graph == null ? "@startuml\n@enduml\n" : new PlantUmlGraphGenerator().Generate(graph));
    }

    private void WriteJsonNew<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, _json);
    }

    private static void WriteTextNew(string path, string value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream);
        writer.Write(value);
    }

    private static byte[] CreatePayload(int size, int seed)
    {
        var result = new byte[size];
        Span<byte> input = stackalloc byte[8];
        for (var index = 0; index < result.Length; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(input, seed);
            BinaryPrimitives.WriteInt32BigEndian(input[4..], index);
            result[index] = SHA256.HashData(input)[0];
        }
        return result;
    }

    private static ExperimentMetricSummary EmptyMetrics() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

    private static string StatusPhase(ExperimentRunStatus status) => status switch
    {
        ExperimentRunStatus.ConnectionFailed => "connect",
        ExperimentRunStatus.SubscriptionFailed => "subscribe",
        ExperimentRunStatus.DrainTimedOut => "drain",
        _ => "run"
    };

    private sealed class ExperimentPhaseException : Exception
    {
        public ExperimentPhaseException(ExperimentRunStatus status, string message) : base(message) =>
            Status = status;
        public ExperimentRunStatus Status { get; }
    }

    private sealed record ExperimentPhaseBoundary(string Name, long StartTick, long EndTick, string Status);
    private sealed record BrokerClientCount(string Broker, int ConnectedClients, int ProfileClients);

    private sealed record ExperimentManifest(
        string RunId,
        DateTimeOffset CreatedAtUtc,
        MqttRelayExperimentOptions Options,
        IReadOnlyList<string?> AssignedBrokerTypes,
        ExperimentProvenance Provenance,
        string OperatingSystem,
        string Runtime,
        int ProcessorCount,
        TimeSpan TickPeriod,
        IReadOnlyList<ExperimentPhaseBoundary> Phases,
        long MeasurementStartTick,
        long MeasurementEndTick,
        IReadOnlyList<BrokerClientCount> BrokerClientCounts);
}
