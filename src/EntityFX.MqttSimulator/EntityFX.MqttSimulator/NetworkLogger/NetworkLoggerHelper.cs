using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;

public static class NetworkLoggerHelper
{
    public static NetworkLoggerScope? WithBeginScope(this INetworkLogger monitoring, ref NetworkPacket packet, string message)
    {
        monitoring.BeginScope(ref packet, message);

        return packet.Scope;
    }

    public static NetworkLoggerScope? WithEndScope(this INetworkLogger monitoring, ref NetworkPacket packet)
    {
        monitoring.EndScope(ref packet);

        return packet?.Scope;
    }

}
