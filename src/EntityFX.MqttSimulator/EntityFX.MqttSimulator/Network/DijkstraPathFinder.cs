// See https://aka.ms/new-console-template for more information
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
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


    public IEnumerable<INetwork> GetPathToNetwork(string sourceNetworkAddress, string destinationNetworkAddress)
    {
        var netPair = $"{sourceNetworkAddress}:{destinationNetworkAddress}";
        var paths = _pathCache.GetValueOrDefault(netPair);
        if (paths != null)
        {
            return paths;
        }

        if (NetworkGraph?.Networks?.Any() != true)
        {
            return Enumerable.Empty<INetwork>();
        }

        var path = DijkstraEngine.CalculateShortestPathBetween(sourceNetworkAddress, destinationNetworkAddress, _paths);


        var networks = path.Select(p => NetworkGraph.Networks.GetValueOrDefault(p.Destination))
            .Where(n => n != null).Select(n => n!);


        var result = networks ?? Enumerable.Empty<INetwork>();
        _pathCache[netPair] = result;

        return result;
    }
}
