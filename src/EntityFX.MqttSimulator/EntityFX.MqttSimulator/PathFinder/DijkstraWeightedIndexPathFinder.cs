// See https://aka.ms/new-console-template for more information
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.PathFinder;

public class DijkstraWeightedIndexPathFinder : IPathFinder
{
    public INetworkSimulator? NetworkGraph { get; set; }

    public Dictionary<string, IEnumerable<INetwork>> _pathCache = new Dictionary<string, IEnumerable<INetwork>>();

    private DijkstraWeightedGraph<INetwork> _di = new DijkstraWeightedGraph<INetwork>();

    public void Build()
    {
        _pathCache.Clear();
        if (NetworkGraph?.Networks?.Any() != true)
        {
            return;
        }

        _di.Clear();

        var visitedNetworks = new HashSet<string>();

        foreach (var network in NetworkGraph.Networks)
        {
            visitedNetworks.Add(network.Key);
            foreach (var nearestNetwork in network.Value.LinkedNearestNetworks)
            {
                if (visitedNetworks.Contains(nearestNetwork.Key)) continue;
                _di.AddEdge(network.Value.Index,
                    network.Value, nearestNetwork.Value.Index,
                    nearestNetwork.Value, 1);
            }
        }
    }


    public IEnumerable<INetwork> GetPath(INode source, INode destination)
    {
        var netPair = $"{source}:{destination}";
        var paths = _pathCache.GetValueOrDefault(netPair);
        if (paths != null)
        {
            return paths;
        }

        if (NetworkGraph?.Networks?.Any() != true)
        {
            return Enumerable.Empty<INetwork>();
        }

        var path = _di.FindShortestPath(source.Index, destination.Index);

        var networks = path.Select(p => p.item);

        var result = networks ?? Enumerable.Empty<INetwork>();
        _pathCache[netPair] = result;

        return result;
    }
}
