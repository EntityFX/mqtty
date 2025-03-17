namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        Task SendAsync(Packet packet);

        Task ReceiveAsync(Packet packet);
    }
}
