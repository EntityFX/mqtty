using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.SignalR;

namespace EntityFX.MqttY.Web.App.Hubs;

/// <summary>
/// Связывает события MonitoringService с SignalR-хабом: рассылает снимки,
/// статусы узлов и смену состояния по группам заданий.
/// </summary>
public sealed class MonitoringHubForwarder : IHostedService
{
    private readonly IMonitoringService _monitoring;
    private readonly IHubContext<MonitoringHub> _hub;

    public MonitoringHubForwarder(IMonitoringService monitoring, IHubContext<MonitoringHub> hub)
    {
        _monitoring = monitoring;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _monitoring.MonitoringUpdated += OnMonitoringUpdated;
        _monitoring.NodeStatusUpdated += OnNodeStatusUpdated;
        _monitoring.StateChanged += OnStateChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _monitoring.MonitoringUpdated -= OnMonitoringUpdated;
        _monitoring.NodeStatusUpdated -= OnNodeStatusUpdated;
        _monitoring.StateChanged -= OnStateChanged;
        return Task.CompletedTask;
    }

    private void OnMonitoringUpdated(string simulationId, MonitoringSnapshotDto snapshot) =>
        _hub.Clients.Group(simulationId).SendAsync("MonitoringUpdate", snapshot);

    private void OnNodeStatusUpdated(string simulationId, NodeStatusDto node) =>
        _hub.Clients.Group(simulationId).SendAsync("NodeStatusUpdate", node);

    private void OnStateChanged(string simulationId, SimulationStatusDto status) =>
        _hub.Clients.Group(simulationId).SendAsync("SimulationStateChanged", status);
}
