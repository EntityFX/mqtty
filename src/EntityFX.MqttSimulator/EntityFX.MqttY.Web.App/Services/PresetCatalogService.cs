using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>
/// Реализация каталога пресетов на JSON-файлах. При первом запуске создаёт
/// seed-пресеты четырёх брокеров и четырёх видов топологий.
/// </summary>
public sealed class PresetCatalogService : IPresetCatalogService
{
    private const string BrokersDir = "presets/brokers";
    private const string TopologiesDir = "presets/topologies";

    private readonly JsonFileStore _store;

    public PresetCatalogService(JsonFileStore store)
    {
        _store = store;
        EnsureSeedData();
        if (GetBrokerPresetAsync("ideal-demo").GetAwaiter().GetResult() == null)
            SaveBrokerPresetAsync(new BrokerPresetDto { Id = "ideal-demo", Name = "Демо: идеальная модель без профиля", BrokerType = "Mosquitto", ProfilePath = "", Description = "Проверка механизма; не двойник реального брокера." }).GetAwaiter().GetResult();
    }

    public async Task<IReadOnlyList<BrokerPresetDto>> GetBrokerPresetsAsync()
    {
        var result = new List<BrokerPresetDto>();
        foreach (var id in _store.ListFiles(BrokersDir, "*.json"))
        {
            var preset = await _store.ReadAsync<BrokerPresetDto>($"{BrokersDir}/{id}.json");
            if (preset != null)
            {
                result.Add(preset);
            }
        }

        return result.OrderBy(p => p.Name).ToList();
    }

    public Task<BrokerPresetDto?> GetBrokerPresetAsync(string id) =>
        _store.ReadAsync<BrokerPresetDto>($"{BrokersDir}/{Sanitize(id)}.json");

    public async Task<BrokerPresetDto> SaveBrokerPresetAsync(BrokerPresetDto preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id))
        {
            preset.Id = Guid.NewGuid().ToString("N");
        }

        await _store.WriteAsync($"{BrokersDir}/{Sanitize(preset.Id)}.json", preset);
        return preset;
    }

    public Task DeleteBrokerPresetAsync(string id) =>
        Task.Run(() => _store.Delete($"{BrokersDir}/{Sanitize(id)}.json"));

    public async Task<IReadOnlyList<TopologyPresetDto>> GetTopologyPresetsAsync()
    {
        var result = new List<TopologyPresetDto>();
        foreach (var id in _store.ListFiles(TopologiesDir, "*.json"))
        {
            var preset = await _store.ReadAsync<TopologyPresetDto>($"{TopologiesDir}/{id}.json");
            if (preset != null)
            {
                result.Add(preset);
            }
        }

        return result.OrderBy(p => p.Name).ToList();
    }

    public Task<TopologyPresetDto?> GetTopologyPresetAsync(string id) =>
        _store.ReadAsync<TopologyPresetDto>($"{TopologiesDir}/{Sanitize(id)}.json");

    public async Task<TopologyPresetDto> SaveTopologyPresetAsync(TopologyPresetDto preset)
    {
        if (string.IsNullOrWhiteSpace(preset.Id))
        {
            preset.Id = Guid.NewGuid().ToString("N");
        }

        await _store.WriteAsync($"{TopologiesDir}/{Sanitize(preset.Id)}.json", preset);
        return preset;
    }

    public Task DeleteTopologyPresetAsync(string id) =>
        Task.Run(() => _store.Delete($"{TopologiesDir}/{Sanitize(id)}.json"));

    private void EnsureSeedData()
    {
        if (!_store.ListFiles(BrokersDir, "*.json").Any())
        {
            SeedBrokers().GetAwaiter().GetResult();
        }

        if (!_store.ListFiles(TopologiesDir, "*.json").Any())
        {
            SeedTopologies().GetAwaiter().GetResult();
        }
    }

    private async Task SeedBrokers()
    {
        var brokers = new[]
        {
            new BrokerPresetDto
            {
                Id = "mosquitto-default",
                Name = "Mosquitto — калибровка v3",
                BrokerType = "Mosquitto",
                ProfilePath = "profiles/Mosquitto/broker-calibration.v3.json",
                Description = "Двойник Mosquitto по натурным наблюдениям",
                Defaults = new BrokerDefaultsDto { PayloadBytes = 16, Qos = 1, Publishers = 64, OfferedRps = 1000 }
            },
            new BrokerPresetDto
            {
                Id = "aedes-default",
                Name = "Aedes — калибровка v3",
                BrokerType = "Aedes",
                ProfilePath = "profiles/Aedes/broker-calibration.v3.json",
                Description = "Двойник Aedes по натурным наблюдениям",
                Defaults = new BrokerDefaultsDto { PayloadBytes = 16, Qos = 1, Publishers = 64, OfferedRps = 1000 }
            },
            new BrokerPresetDto
            {
                Id = "activemq-default",
                Name = "ActiveMQ — калибровка v3",
                BrokerType = "ActiveMQ",
                ProfilePath = "profiles/ActiveMQ/broker-calibration.v3.json",
                Description = "Двойник ActiveMQ по натурным наблюдениям",
                Defaults = new BrokerDefaultsDto { PayloadBytes = 16, Qos = 1, Publishers = 64, OfferedRps = 1000 }
            },
            new BrokerPresetDto
            {
                Id = "emqx-default",
                Name = "EMQX — калибровка v3",
                BrokerType = "EMQX",
                ProfilePath = "profiles/EMQX/broker-calibration.v3.json",
                Description = "Двойник EMQX по натурным наблюдениям",
                Defaults = new BrokerDefaultsDto { PayloadBytes = 16, Qos = 1, Publishers = 64, OfferedRps = 1000 }
            }
        };

        foreach (var broker in brokers)
        {
            await SaveBrokerPresetAsync(broker);
        }
    }

    private async Task SeedTopologies()
    {
        var topologies = new[]
        {
            new TopologyPresetDto
            {
                Id = "line-3-mosquitto",
                Name = "Линия из 3 брокеров Mosquitto",
                Kind = "line",
                ChainLength = 3,
                BrokerPresetId = "mosquitto-default",
                ClientsPerBroker = 2,
                RelayApps = true,
                Engine = "relay"
            },
            new TopologyPresetDto
            {
                Id = "ring-4-mosquitto",
                Name = "Кольцо из 4 брокеров Mosquitto",
                Kind = "ring",
                RingLength = 4,
                BrokerPresetId = "mosquitto-default",
                ClientsPerBroker = 2,
                RelayApps = true,
                Engine = "relay"
            },
            new TopologyPresetDto
            {
                Id = "star-3-aedes",
                Name = "Звезда из 3 лучей Aedes",
                Kind = "star",
                Rays = 3,
                ChainLength = 1,
                BrokerPresetId = "aedes-default",
                ClientsPerBroker = 2,
                RelayApps = true,
                Engine = "federation"
            },
            new TopologyPresetDto
            {
                Id = "combined-4-3",
                Name = "Комбинированная: кольцо 4 + 3 луча",
                Kind = "combined",
                RingLength = 4,
                Rays = 3,
                ChainLength = 2,
                BrokerPresetId = "mosquitto-default",
                ClientsPerBroker = 2,
                RelayApps = true,
                Engine = "loadBalancing"
            }
        };

        foreach (var topology in topologies)
        {
            await SaveTopologyPresetAsync(topology);
        }
    }

    private static string Sanitize(string id) =>
        string.Concat(id.Where(char.IsLetterOrDigit).Select(c => c));
}
