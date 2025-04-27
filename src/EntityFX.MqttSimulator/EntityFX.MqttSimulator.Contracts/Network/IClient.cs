namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : ISender, ILeafNode
    {
        bool IsConnected { get; }

        bool Connect(string server);

        bool Disconnect();


        event EventHandler<NetworkPacket>? PacketReceived;

        bool Send(byte[] packet, string? category = null);
    }
}
