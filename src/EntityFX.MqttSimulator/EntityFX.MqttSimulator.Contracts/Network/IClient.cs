namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : ISender, ILeafNode, IStagedClient
    {
        bool IsConnected { get; }

        bool Connect(string server);

        bool Disconnect();

        event EventHandler<INetworkPacket>? PacketReceived;

        bool Send(byte[] packet, string? category = null);
    }
}
