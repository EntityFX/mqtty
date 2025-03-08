namespace EntityFX.MqttY.Contracts.Options;

public class ScenarioOption
{
    public string Type { get; set; } = String.Empty;
    
    public SortedDictionary<string, ScenarioActionOption> Actions { get; set; } = new();
}