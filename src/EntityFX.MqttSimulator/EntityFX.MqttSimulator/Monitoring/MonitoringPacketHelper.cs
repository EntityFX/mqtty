using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;

public static class MonitoringPacketHelper
{
    public static MonitoringScope? WithBeginScope(this IMonitoring monitoring, ref NetworkPacket packet, string message)
    {
        monitoring.BeginScope(ref packet, message);

        return packet.Scope;
    }

    public static MonitoringScope? WithEndScope(this IMonitoring monitoring, ref NetworkPacket packet)
    {
        monitoring.EndScope(ref packet);

        return packet?.Scope;
    }

}
