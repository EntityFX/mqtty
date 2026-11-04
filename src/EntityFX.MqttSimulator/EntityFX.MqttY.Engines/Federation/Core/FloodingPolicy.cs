namespace EntityFX.MqttY.Federation;

public sealed class FloodingPolicy : IForwardingPolicy
{
    private readonly SiteLink[] edges;
    public FloodingPolicy(SiteLink[] edges) => this.edges = edges;
    public int[] Destinations(int site, int origin, int sender) => FederationGraph.Neighbors(site, edges).Where(n => n != sender).ToArray();
}
