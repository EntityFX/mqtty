namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public enum NetworkLoggerType
    {
        Send,
        Receive,
        Transfer,
        Push,
        Delivery,
        Response,
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
