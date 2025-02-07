// See https://aka.ms/new-console-template for more information
using EntityFX.MqttY.Contracts.Network;

public class SimplePathFinder : IPathFinder
{

    public INetworkGraph? NetworkGraph { get; set; }

    public void Build()
    {

    }

    public IEnumerable<INetwork> GetPathToNetwork(string sourceNetworkAddress, string destinationNetworkAddress)
    {
        if (NetworkGraph == null)
        {
            return Enumerable.Empty<INetwork>();
        }

        if (!NetworkGraph.Networks.ContainsKey(destinationNetworkAddress))
        {
            return Enumerable.Empty<INetwork>();
        }

        if (!NetworkGraph.Networks.ContainsKey(sourceNetworkAddress))
        {
            return Enumerable.Empty<INetwork>();
        }

        var path = new List<INetwork>();
        var result = GetPathToNetworkWeighted(sourceNetworkAddress, destinationNetworkAddress);
        return result;
    }

    private IEnumerable<INetwork> GetPathToNetworkWeighted(string sourceNetworkAddress, string destinationNetworkAddress)
    {
        var except = new List<string>();
        var allPaths = new List<List<INetwork>>();
        var length = 0;
        do
        {
            var path = new List<INetwork>();
            FindNodeNetworkWithExcept(null, NetworkGraph.Networks[sourceNetworkAddress], destinationNetworkAddress, path, except);

            if (path.Count == 0)
            {
                break;
            }

            allPaths.Add(path);
            length = path.Count;
        }
        while (length > 0);

        var shortest = allPaths.Select(p => (p.Count, p)).OrderBy(p => p.Count).FirstOrDefault();

        return shortest.p ?? Enumerable.Empty<INetwork>();
    }


    private bool FindNodeNetworkWithExcept(
        INetwork? previous, INetwork network, string destinationNode, List<INetwork> path, List<string> except)
    {
        if (network.Address == destinationNode)
        {
            except.Remove(network.Address);
            return true;
        }

        except.Add(network.Address);
        foreach (var nn in network.LinkedNearestNetworks)
        {
            if (except.Contains(nn.Key))
            {
                continue;
            }

            if (nn.Key.Equals(previous?.Address))
            {
                continue;
            }

            path.Add(nn.Value);

            return FindNodeNetworkWithExcept(network, nn.Value, destinationNode, path, except);
        }

        return false;
    }
}
