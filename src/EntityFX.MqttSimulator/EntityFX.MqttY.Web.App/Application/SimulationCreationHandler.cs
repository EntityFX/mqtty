using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;

namespace EntityFX.MqttY.Web.App.Application;

/// <summary>Creates a simulation job from a preset/topology or an edited graph.</summary>
public sealed class SimulationCreationHandler
{
    private readonly IPresetCatalogService _catalog;
    private readonly ITopologyGeneratorService _generator;
    private readonly IConfigurationMapperService _mapper;
    private readonly ISimulationJobRunner _runner;
    private readonly DataDirectoryOptions _options;
    private readonly NativeConfigurationService _native;
    private readonly JsonFileStore _store;

    public SimulationCreationHandler(
        IPresetCatalogService catalog,
        ITopologyGeneratorService generator,
        IConfigurationMapperService mapper,
        ISimulationJobRunner runner,
        DataDirectoryOptions options,
        NativeConfigurationService native,
        JsonFileStore store)
    {
        _catalog = catalog;
        _generator = generator;
        _mapper = mapper;
        _runner = runner;
        _options = options;
        _native = native;
        _store = store;
    }

    public async Task<ApplicationResult<object>> HandleAsync(SimulationCreateDto request)
    {
        try
        {
            if (request == null) return ApplicationResult<object>.BadRequest("Request body is required.");
            if (request.RunSettings == null) return ApplicationResult<object>.BadRequest("RunSettings is required.");
            SimulationStorageGuard.Check(_options, Path.GetFullPath(_options.DataDirectory));
            if (request.Engine is not ("relay" or "federation" or "loadBalancing"))
                return ApplicationResult<object>.BadRequest("Unknown engine.");
            if (request.RunSettings.ExecutionMode is not ("" or "InProcess" or "OutOfProcess"))
                return ApplicationResult<object>.BadRequest("Unknown execution mode.");

            var spec = await ResolveSpecAsync(request);
            if (spec == null)
                return ApplicationResult<object>.BadRequest("Topology not found or graph is empty.");

            var saved = request.TopologyId == null
                ? null
                : await _store.ReadAsync<SavedTopologyDto>($"topologies/{request.TopologyId}.json");
            var supplied = request.Graph ?? saved?.Graph;
            if (supplied != null)
            {
                var ids = supplied.Nodes.Select(n => n.Id).Concat(supplied.Networks.Select(n => n.Id)).ToArray();
                if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length)
                    throw new ArgumentException("Идентификаторы узлов и сетей должны быть уникальными и непустыми.");
            }

            var expected = _generator.Generate(spec);
            var edited = supplied != null && GraphStructure(supplied) != GraphStructure(_mapper.ToGraphDto(expected));
            var graph = edited ? _mapper.ToNetworkGraphOption(supplied!) : expected;
            var runOptions = _mapper.ToRunOptions(graph, request.Engine, spec);
            await _native.PopulateAsync(runOptions, spec, edited);
            if (request.ValidateOnly)
            {
                return ApplicationResult<object>.Ok(new
                {
                    valid = true,
                    editedGraph = edited,
                    networks = graph.Networks.Count,
                    brokers = graph.Nodes.Values.Count(n =>
                        n.Type == EntityFX.MqttY.Contracts.Options.NodeOptionType.Server)
                });
            }

            var job = new SimulationJob
            {
                TopologyId = request.TopologyId,
                Name = saved?.Name,
                Engine = request.Engine,
                RunOptions = runOptions,
                TimeLimit = request.RunSettings.TimeLimit ?? _options.DefaultTimeLimit,
                Parallel = request.RunSettings.Parallel || _options.DefaultParallel,
                ExecutionMode = string.IsNullOrWhiteSpace(request.RunSettings.ExecutionMode)
                    ? _options.DefaultExecutionMode
                    : request.RunSettings.ExecutionMode
            };

            await _runner.EnqueueAsync(job);
            return ApplicationResult<object>.Accepted(SimulationDtoMapper.ToStatusDto(job));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException
            or KeyNotFoundException or InvalidOperationException)
        {
            return ApplicationResult<object>.BadRequest(ex.Message);
        }
    }

    private async Task<TopologySpecDto?> ResolveSpecAsync(SimulationCreateDto request)
    {
        if (request.Spec != null) { request.Spec.Engine = request.Engine; return request.Spec; }
        if (request.Graph != null)
        {
            return new TopologySpecDto { Engine = request.Engine };
        }

        if (string.IsNullOrWhiteSpace(request.TopologyId))
        {
            return null;
        }

        var preset = await _catalog.GetTopologyPresetAsync(request.TopologyId);
        if (preset == null)
        {
            var saved = await _store.ReadAsync<SavedTopologyDto>($"topologies/{request.TopologyId}.json");
            if (saved == null) return null;
            saved.Spec.Engine = request.Engine;
            return saved.Spec;
        }

        return new TopologySpecDto
        {
            Kind = preset.Kind,
            ChainLength = preset.ChainLength,
            Rays = preset.Rays,
            RingLength = preset.RingLength,
            BrokerPresetId = preset.BrokerPresetId,
            ClientsPerBroker = preset.ClientsPerBroker,
            RelayApps = preset.RelayApps,
            Engine = request.Engine,
            NetworkLength = preset.NetworkLength,
            Qos = preset.Qos,
            PayloadBytes = preset.PayloadBytes,
            OfferedRps = preset.OfferedRps,
            WarmupTicks = preset.WarmupTicks,
            MeasurementTicks = preset.MeasurementTicks,
            DrainTimeoutTicks = preset.DrainTimeoutTicks,
            Seed = preset.Seed,
            NetworkType = preset.NetworkType
        };
    }

    private static string GraphStructure(GraphDto graph)
    {
        var normalized = new
        {
            graph.RandomSeed,
            graph.Ticks,
            Networks = graph.Networks.OrderBy(n => n.Id).Select(n => new { n.Id, n.NetworkType }),
            Nodes = graph.Nodes.OrderBy(n => n.Id).Select(n => new
            {
                n.Id, n.Type, n.Specification, n.Network, n.Broker, n.ConnectsToServer,
                n.Configuration, n.Additional
            }),
            Links = graph.Links.OrderBy(l => l.From).ThenBy(l => l.To).Select(l => new { l.From, l.To, l.Weight }),
            graph.NetworkTypes
        };
        return System.Text.Json.JsonSerializer.Serialize(normalized,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
