namespace EntityFX.MqttY.Contracts.Network
{
    public interface IPathFinder
    {
        INetworkSimulator? NetworkGraph { get; set; }

        void Build();

        IEnumerable<INetwork> GetPathToNetwork(string sourceNetworkAddress, string destinationNetworkAddress);
    }
}
