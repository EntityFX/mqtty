namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Граф топологии для редактора.</summary>
public sealed class GraphDto
{
    public int RandomSeed { get; set; }
    public EntityFX.MqttY.Contracts.Options.TicksOptions? Ticks { get; set; }
    public SortedDictionary<string, EntityFX.MqttY.Contracts.Options.NetworkOptions> NetworkTypes { get; set; } = new();
    public List<NetworkDto> Networks { get; set; } = new();

    public List<NodeDto> Nodes { get; set; } = new();

    public List<LinkDto> Links { get; set; } = new();
}
