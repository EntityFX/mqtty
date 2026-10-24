using System.Text.Json;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Явное преобразование поддерживаемых параметров UI в конфигурации научных runners.</summary>
public sealed class NativeConfigurationService
{
    private readonly IPresetCatalogService _catalog;
    private readonly JsonFileStore _store;
    public NativeConfigurationService(IPresetCatalogService catalog, JsonFileStore store) { _catalog = catalog; _store = store; }

    public async Task PopulateAsync(SimulationRunOptions run, TopologySpecDto spec, bool editedGraph = false)
    {
        var preset = await _catalog.GetBrokerPresetAsync(spec.BrokerPresetId);
        if (preset == null) throw new InvalidDataException("Broker preset not found.");
        var count = run.Graph.Nodes.Values.Count(n => n.Type == EntityFX.MqttY.Contracts.Options.NodeOptionType.Server);
        var layout=editedGraph?EditedGraphLayout.Read(run.Graph):null;
        if(layout!=null) {
            var used=run.Graph.Networks.Values.Select(n=>run.Graph.NetworkTypes[n.NetworkType]).ToArray();
            var network=used[0];
            if(used.Any(n=>n.CapacityBytesPerSecond!=network.CapacityBytesPerSecond || n.QueueCapacity!=network.QueueCapacity || n.TransferTicks!=network.TransferTicks || n.ThroughputWindowTicks!=network.ThroughputWindowTicks))
                throw new ArgumentException("Для составного опыта пока нужны одинаковые параметры сетей. Разные скорости и очереди не подменяются общим значением.");
            spec.NetworkType=new NetworkTypeDto {Name=network.NetworkType,CapacityBytesPerSecond=checked((long)network.CapacityBytesPerSecond),QueueCapacity=network.QueueCapacity,TransferTicks=network.TransferTicks,ThroughputWindowTicks=network.ThroughputWindowTicks};
        }
        var publishers = layout?.PublisherSites.Length ?? checked(count * spec.ClientsPerBroker);
        var tick = run.Graph.Ticks.TickPeriod;
        var profile = string.IsNullOrWhiteSpace(preset.ProfilePath) ? null : _store.Resolve(preset.ProfilePath);
        // Read profile once into a content-addressed immutable input. Scientific runners use this snapshot.
        if (profile != null)
        {
            var bytes = await File.ReadAllBytesAsync(profile);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            run.ExpectedProfileSha256 = sha;
            var snapshot = _store.Resolve($"profile-snapshots/{sha}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            if (!File.Exists(snapshot))
            {
                try { await using var stream = new FileStream(snapshot, FileMode.CreateNew, FileAccess.Write, FileShare.None); await stream.WriteAsync(bytes); }
                catch (IOException) when (File.Exists(snapshot)) { }
            }
            if (!System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(snapshot)).SequenceEqual(System.Security.Cryptography.SHA256.HashData(bytes)))
                throw new InvalidDataException("Profile snapshot integrity failure.");
            profile = snapshot;
        }
        if (run.Engine == "loadBalancing")
        {
            if(editedGraph)throw new ArgumentException("Балансировщик пока исполняет только свою backend-топологию. Для произвольных сетей выберите MqttRelay или федерацию.");
            if (spec.Kind != "line")
                throw new InvalidDataException("Load balancing uses the existing backend-branch topology. Select line; ring/star/combined are not silently substituted.");
            var options = new LoadBalancingOptions
            {
                Mode = Enum.TryParse<BalancingMode>(spec.BalancingMode, true, out var mode) ? mode : throw new InvalidDataException("Unknown balancing mode."),
                BackendCount = count, TotalPublishers = publishers, Ideal = profile == null, BrokerType = preset.BrokerType,
                BrokerProfilePath = profile, PayloadBytes = spec.PayloadBytes, PublishQos = spec.Qos, SubscribeQos = spec.Qos,
                OfferedRps = spec.OfferedRps, Seed = spec.Seed, TickPeriod = tick,
                WarmupSeconds = spec.WarmupTicks * tick.TotalSeconds, MeasurementSeconds = spec.MeasurementTicks * tick.TotalSeconds,
                DrainTimeoutSeconds = spec.DrainTimeoutTicks * tick.TotalSeconds, NetworkLength = spec.NetworkLength,
                Network = new NetworkSettings { CapacityBytesPerSecond = spec.NetworkType.CapacityBytesPerSecond,
                    QueueCapacity = checked((int)spec.NetworkType.QueueCapacity), TransferTicks = spec.NetworkType.TransferTicks,
                    ThroughputWindowTicks = spec.NetworkType.ThroughputWindowTicks }
            };
            run.NativeConfiguration = JsonSerializer.SerializeToElement(options, ExperimentInput.Json);
        }
        else
        {
            if (!editedGraph && spec.Kind == "combined") throw new InvalidDataException("Combined topology is not supported by the native runner yet.");
            if (!editedGraph && spec.Kind == "star" && spec.ChainLength != 1) throw new InvalidDataException("Native star requires one transit network; use chainLength=1.");
            var options = new FederationOptions
            {
                Mode = run.Engine == "relay" ? FederationMode.Relay : FederationMode.Federation,
                Brokers = count, Publishers = publishers, Topology = layout == null ? spec.Kind : "custom", Ideal = profile == null,
                PhysicalLayout=layout?.Physical, Links=layout?.Links, PublisherSites=layout?.PublisherSites,
                BrokerType = preset.BrokerType, BrokerProfilePath = profile, PayloadBytes = spec.PayloadBytes, Qos = spec.Qos,
                OfferedRps = spec.OfferedRps, Seed = spec.Seed, TickPeriod = tick,
                WarmupSeconds = spec.WarmupTicks * tick.TotalSeconds, MeasurementSeconds = spec.MeasurementTicks * tick.TotalSeconds,
                DrainTimeoutSeconds = spec.DrainTimeoutTicks * tick.TotalSeconds,
                Network = new FederationNetworkOptions { CapacityBytesPerSecond = checked((long)spec.NetworkType.CapacityBytesPerSecond),
                    QueueCapacity = checked((int)spec.NetworkType.QueueCapacity), TransferTicks = spec.NetworkType.TransferTicks,
                    ThroughputWindowTicks = spec.NetworkType.ThroughputWindowTicks }
            };
            run.NativeConfiguration = JsonSerializer.SerializeToElement(options, FederationInput.Json);
        }
        NativeExperimentAdapter.Run(run, null, validateOnly: true);
    }
}
