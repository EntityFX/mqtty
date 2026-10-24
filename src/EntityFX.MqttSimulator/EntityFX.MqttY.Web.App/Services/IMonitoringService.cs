using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Сервис мониторинга симуляций: снимки, статусы узлов, лог, события для SignalR.</summary>
public interface IMonitoringService
{
    void Update(string simulationId, EngineProgress progress);

    void SetState(string simulationId, string state);

    MonitoringSnapshotDto? GetSnapshot(string simulationId);

    NodeStatusDto? GetNode(string simulationId, string nodeId);

    IReadOnlyList<LogItemDto> GetLog(string simulationId, int offset);

    event Action<string, MonitoringSnapshotDto>? MonitoringUpdated;

    event Action<string, NodeStatusDto>? NodeStatusUpdated;

    event Action<string, SimulationStatusDto>? StateChanged;
}
