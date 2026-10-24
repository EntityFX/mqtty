using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Преобразование между GraphDto (редактор), NetworkGraphOption (дизайн) и execution-конфигом.</summary>
public interface IConfigurationMapperService
{
    GraphDto ToGraphDto(NetworkGraphOption option);

    NetworkGraphOption ToNetworkGraphOption(GraphDto graph);

    SimulationRunOptions ToRunOptions(NetworkGraphOption graph, string engine, TopologySpecDto spec);
}
