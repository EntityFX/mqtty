namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        Task<Packet> ReceiveWithResponseAsync(Packet packet);

        Task<Packet> SendWithResponseAsync(Packet packet);

        Task SendAsync(Packet packet);

        Task ReceiveAsync(Packet packet);
    }
}
