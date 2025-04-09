namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public enum NetworkLoggerType
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
        Refresh,
        Reset,
    }
}
