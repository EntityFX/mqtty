using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.PathFinder;
using System.Xml.Linq;
public class DijkstraPathFinder : IPathFinder
{
    public INetworkSimulator? NetworkGraph { get; set; }

    private Path<string>[] _paths = new Path<string>[0];

    public Dictionary<string, IEnumerable<INetwork>> _pathCache = new Dictionary<string, IEnumerable<INetwork>>();

    public void Build()
    {
        _pathCache.Clear();
        if (NetworkGraph?.Networks?.Any() != true)
        {
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


        var result = networks ?? Enumerable.Empty<INetwork>();
        _pathCache[netPair] = result;

        return result;
    }
}
