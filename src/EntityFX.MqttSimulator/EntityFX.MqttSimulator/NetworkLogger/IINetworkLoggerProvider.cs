using EntityFX.MqttY.Contracts.NetworkLogger;

internal interface IINetworkLoggerProvider
{
    void Start();

    void ItemAdded(NetworkLoggerItem item);
    void ScopeEnded(NetworkLoggerScope scope);
    void ScopeStarted(NetworkLoggerScope scope);
}