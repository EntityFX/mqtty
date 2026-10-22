namespace EntityFX.MqttY.Federation;

public sealed class TreeForwardingPolicy : IForwardingPolicy
{
    private readonly SiteLink[][] trees;
    public TreeForwardingPolicy(int sites, SiteLink[] edges) => trees = Enumerable.Range(0, sites).Select(root => FederationGraph.Tree(root, sites, edges)).ToArray();
    public int[] Destinations(int site, int origin, int sender) => trees[origin].Where(e => e.A == site).Select(e => e.B).ToArray();
}
