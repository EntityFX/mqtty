using Microsoft.AspNetCore.SignalR;

namespace EntityFX.MqttY.Web.App.Hubs;

/// <summary>SignalR-хаб мониторинга: подписка на события задания по simulationId.</summary>
public sealed class MonitoringHub : Hub
{
    public Task SubscribeMonitoring(string simulationId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, simulationId);

    public Task UnsubscribeMonitoring(string simulationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, simulationId);
}
