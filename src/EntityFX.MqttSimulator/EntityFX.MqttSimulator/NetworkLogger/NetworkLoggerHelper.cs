using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;

public static class NetworkLoggerHelper
{
    public static NetworkLoggerScope? WithBeginScope(this INetworkLogger monitoring, long tick, ref NetworkPacket packet, string message)
    {
        monitoring.BeginScope(tick, ref packet, message);

        return packet.Scope;
    }

    public static NetworkLoggerScope? WithEndScope(this INetworkLogger monitoring, long tick, ref NetworkPacket packet)
    {
        monitoring.EndScope(tick, ref packet);

        return packet.Scope;
    }

}
