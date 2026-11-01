using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.PathFinder;
using System.Collections.Concurrent;

public class DijkstraIndexPathFinder : IPathFinder
{
    public INetworkSimulator? NetworkGraph { get; set; }

    private volatile Path<int>[] _paths = new Path<int>[0];

    private readonly ConcurrentDictionary<string, IEnumerable<INetwork>> _pathCache = new();

    public void Build()
    {
        if (NetworkGraph?.Networks?.Any() != true)
        {
            _paths = new Path<int>[0];
            _pathCache.Clear();
            return;
        }

        var pathsList = new List<Path<int>>();
        foreach (var source in NetworkGraph.Networks)
        {
            foreach (var destination in source.Value.LinkedNearestNetworks)
            {
                pathsList.Add(new Path<int>(source.Value.Index, destination.Value.Index) { Cost = 1 });
            }
        }
        _paths = pathsList.ToArray();
        _pathCache.Clear();
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

        var path = DijkstraEngine.CalculateShortestPathBetween(
            source.Index, destination.Index, _paths);


        var networks = path.Select(p =>
            NetworkGraph.Networks.Values.FirstOrDefault(n => n.Index == p.Destination))
            .Where(n => n != null).Select(n => n!);

        var result = (networks ?? Enumerable.Empty<INetwork>()).ToArray();
        _pathCache[netPair] = result;

        return result;
    }
}