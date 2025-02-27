namespace EntityFX.MqttY.Contracts.Network
{
    public interface IApplication : INode, ILeafNode
    {
        IReadOnlyDictionary<string, IServer> Servers { get; }
        IReadOnlyDictionary<string, IClient> Clients { get; }

        bool IsStarted { get; }

        bool AddServer(IServer server);

        bool RemoveServer(string server);

        bool AddClient(IClient client);

        bool RemoveClient(string clientAddress);

        Task StartAsync();

        Task StopAsync();
    }
}
