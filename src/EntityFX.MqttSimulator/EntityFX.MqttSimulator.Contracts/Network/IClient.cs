namespace EntityFX.MqttY.Contracts.Network
{
    public interface IClient : INode
    {
        INetwork? Network { get; set; }

        bool IsConnected { get; }

        bool Connect(string server);

        bool Disconnect();


        event EventHandler<(string Client, byte[] Packet)>? PacketReceived;

        Task SendAsync(byte[] packet);

        void Send(byte[] packet);
    }
}
