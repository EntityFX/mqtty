using EntityFX.MqttY.Contracts.NetworkLogger;

internal class NullNetworkLoggerProvider : NetworkLoggerBase, INetworkLoggerProvider
{
    public NullNetworkLoggerProvider(INetworkLogger monitoring)
    : base(monitoring)
    {

    }

    protected override void WriteItem(NetworkLoggerItem item)
    {

    }

    protected override void WriteScope(NetworkLoggerScope scope)
    {

    }
}
