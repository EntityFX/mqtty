using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Каталог пресетов брокеров и топологий.</summary>
public interface IPresetCatalogService
{
    Task<IReadOnlyList<BrokerPresetDto>> GetBrokerPresetsAsync();

    Task<BrokerPresetDto?> GetBrokerPresetAsync(string id);

    Task<BrokerPresetDto> SaveBrokerPresetAsync(BrokerPresetDto preset);

    Task DeleteBrokerPresetAsync(string id);

    Task<IReadOnlyList<TopologyPresetDto>> GetTopologyPresetsAsync();

    Task<TopologyPresetDto?> GetTopologyPresetAsync(string id);

    Task<TopologyPresetDto> SaveTopologyPresetAsync(TopologyPresetDto preset);

    Task DeleteTopologyPresetAsync(string id);
}
