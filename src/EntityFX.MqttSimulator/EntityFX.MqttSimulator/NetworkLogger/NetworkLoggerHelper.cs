using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;

public static class NetworkLoggerHelper
{
    public static NetworkLoggerScope? WithBeginScope<TContext>(this INetworkLogger monitoring, long tick, ref NetworkPacket<TContext> packet, string message)
    {
        monitoring.BeginScope(tick, ref packet, message);

        return packet.Scope;
    }

    public static NetworkLoggerScope? WithEndScope<TContext>(this INetworkLogger monitoring, long tick, ref NetworkPacket<TContext> packet)
    {
        monitoring.EndScope(tick, ref packet);

        return packet.Scope;
    }

    public static NetworkLoggerScope? WithBeginScope(this INetworkLogger monitoring, long tick, ref INetworkPacket packet, string message)
    {
        monitoring.BeginScope(tick, ref packet, message);

        return packet.Scope;
    }

    public static NetworkLoggerScope? WithEndScope(this INetworkLogger monitoring, long tick, ref INetworkPacket packet)
    {
        monitoring.EndScope(tick, ref packet);

        return packet.Scope;
    }

    public static string GetMonitoringLine(this NetworkLoggerItem item) => $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:O}> " +
        $"(LogTick={item.Tick}, Time={item.SimulationTime}, {item.Id}) " +
        $"{{{item.Type}}} " +
        $"{(!string.IsNullOrEmpty(item.Category) ? $"[Category={item.Category}] " : "")}" +
        $"{(item.Ttl != null ? $"{{Ttl={item.Ttl}}} " : "")} " +
        $"{(item.QueueLength != null ? $"{{Queue={item.QueueLength}}} " : "")} " +
        $"{item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
        $"{(item.PacketSize > 0 ? $", NetworkMonitoringPacket Size={item.PacketSize}" : "")}" +
        $"{(!string.IsNullOrEmpty(item.Message) ? $", Message={item.Message}" : "")}";

}
