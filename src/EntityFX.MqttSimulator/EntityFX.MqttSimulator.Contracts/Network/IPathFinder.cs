namespace EntityFX.MqttY.Contracts.Network
{
    public interface IPathFinder
    {
        INetworkGraph? NetworkGraph { get; set; }

        void Build();

        IEnumerable<INetwork> GetPathToNetwork(string sourceNetworkAddress, string destinationNetworkAddress);
    }
}
