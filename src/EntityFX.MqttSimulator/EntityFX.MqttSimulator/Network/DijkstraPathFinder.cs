// See https://aka.ms/new-console-template for more information
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
public class DijkstraPathFinder : IPathFinder
{
    public INetworkGraph? NetworkGraph { get; set; }

    private Path<string>[] paths;

    public void Build()
    {
        var pathsList = new List<Path<string>>();
        foreach (var source in NetworkGraph.Networks)
        {
            foreach (var destination in source.Value.LinkedNearestNetworks)
            {
                pathsList.Add(new Path<string>() { Source = source.Key, Destination = destination.Key, Cost = 1 });
            }
        }
        paths = pathsList.ToArray();
    }


    public IEnumerable<INetwork> GetPathToNetwork(string sourceNetworkAddress, string destinationNetworkAddress)
    {
        var path = DijkstraEngine.CalculateShortestPathBetween(sourceNetworkAddress, destinationNetworkAddress, paths);


        var networks = path.Select(p => NetworkGraph.Networks.GetValueOrDefault(p.Destination));
  

        return networks;
    }
}
