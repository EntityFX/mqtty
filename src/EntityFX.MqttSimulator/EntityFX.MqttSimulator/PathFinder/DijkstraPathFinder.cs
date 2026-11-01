using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.PathFinder;
using System.Collections.Concurrent;
public class DijkstraPathFinder : IPathFinder
{
    public INetworkSimulator? NetworkGraph { get; set; }

    private volatile Path<string>[] _paths = new Path<string>[0];

    // Path lookup runs from Network.Send during parallel refresh (strategy 1),
    // so the cache must be thread-safe; concurrent duplicate computations of the
    // same pair are harmless because Dijkstra is deterministic.
    private readonly ConcurrentDictionary<string, IEnumerable<INetwork>> _pathCache = new();

    public void Build()
    {
        if (NetworkGraph?.Networks?.Any() != true)
        {
            _paths = new Path<string>[0];
            _pathCache.Clear();
            return;
        }

        var pathsList = new List<Path<string>>();
        foreach (var source in NetworkGraph.Networks)
        {
            foreach (var destination in source.Value.LinkedNearestNetworks)
            {
                pathsList.Add(new Path<string>(source.Key, destination.Key) { Cost = 1 });
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

        var path = DijkstraEngine.CalculateShortestPathBetween(source.Name, destination.Name, _paths);

        var networks = path.Select(p =>
        {
            NetworkGraph.Networks.TryGetValue(p.Destination, out var net);
            return net;
        })
            .Where(n => n != null).Select(n => n!);

        var result = (networks ?? Enumerable.Empty<INetwork>()).ToArray();
        _pathCache[netPair] = result;

        return result;
    }
}
