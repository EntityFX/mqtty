namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : INode, ILeafNode
    {
        bool IsConnected { get; }

        bool Connect(string server);

        bool Disconnect();


        event EventHandler<(string Client, byte[] Packet)>? PacketReceived;

        Task<byte[]> SendAsync(byte[] packet, string? category = null);

        byte[] Send(byte[] packet, string? category = null);
    }
}
