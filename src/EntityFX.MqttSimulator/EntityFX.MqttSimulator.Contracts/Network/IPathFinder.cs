namespace EntityFX.MqttY.Contracts.Network
{
    public interface IPathFinder<TIndexType, TResult>
    {
        INetworkSimulator? NetworkGraph { get; set; }

        void Build();

        IEnumerable<TResult> GetPath(TIndexType source, TIndexType destination);
    }

    public interface IPathFinder : IPathFinder<INode, INetwork>
    {
    }
}
