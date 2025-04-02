namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : ISender, ILeafNode
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(string server);

        bool Disconnect();


        event EventHandler<NetworkPacket>? PacketReceived;

        Task SendAsync(byte[] packet, string? category = null);

        void Send(byte[] packet, string? category = null);
    }
}
