namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public enum NetworkLoggerType
    {
        Send,
        Receive,
        Transfer,
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
