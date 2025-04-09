namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public interface INetworkLoggerItem
    {
        Guid Id { get; }

        NetworkLoggerItemType ItemType { get; }
    }
}
