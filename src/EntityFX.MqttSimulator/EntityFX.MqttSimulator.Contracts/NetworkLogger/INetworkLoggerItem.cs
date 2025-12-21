namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public interface INetworkLoggerItem
    {
        long Id { get; }

        NetworkLoggerItemType ItemType { get; }
    }
}
