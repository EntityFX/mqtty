using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;

public static class MonitoringPacketHelper
{
    public static MonitoringScope? WithBeginScope(this IMonitoring monitoring, ref Packet packet, string message)
    {
        monitoring.TryBeginScope(ref packet, message);

        return packet.Scope;
    }

    public static MonitoringScope? WithEndScope(this IMonitoring monitoring, ref Packet packet)
    {
        monitoring.TryEndScope(ref packet);

        return packet.Scope;
    }

}
