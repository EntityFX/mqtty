namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        Task<bool> SendAsync(NetworkPacket packet);

        Task ReceiveAsync(NetworkPacket packet);
    }
}
