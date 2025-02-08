namespace EntityFX.MqttY.Contracts.Network
{
    public interface IServer : INode, ILeafNode
    {
        bool IsStarted { get; }

        event EventHandler<Packet>? PacketReceived;

        event EventHandler<IClient>? ClientConnected;

        event EventHandler<string>? ClientDisconnected;

        IEnumerable<IClient> GetServerClients();

        bool AttachClient(IClient client);

        bool DetachClient(string clientAddress);

        void Start();

        void Stop();
    }
}
