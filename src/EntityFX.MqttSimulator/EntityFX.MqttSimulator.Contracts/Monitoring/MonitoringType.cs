namespace EntityFX.MqttY.Contracts.Monitoring
{
    public enum MonitoringType
    {
        Send,
        Receive,
        Push,
        Unreachable,
        Connect,
        Disconnect,
        Link,
        Unlink,
        DestinationUnreachable,
        Refresh
    }
}
