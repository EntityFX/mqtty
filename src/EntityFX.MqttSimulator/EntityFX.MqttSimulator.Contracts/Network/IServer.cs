namespace EntityFX.MqttY.Contracts.Network
{
    public interface IServer : INode
    {
        INetwork Network { get; }

        bool IsStarted { get; }

        event EventHandler<Packet>? PacketReceived;

        event EventHandler<IClient>? ClientConnected;

        event EventHandler<string>? ClientDisconnected;

        IEnumerable<IClient> GetServerClients();

        bool AttachClient(IClient client);

        bool DetachClient(string address);

        void Start();

        void Stop();
    }
}
