namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        Task SendAsync(NetworkPacket packet);

        Task ReceiveAsync(NetworkPacket packet);
    }
}
