namespace EntityFX.MqttY.Contracts.Options;

public class ScenariosOptions
{
    public string StartScenario { get; set; } = String.Empty;
        
    public SortedDictionary<string, ScenarioOption> Scenarios { get; set; } = new();
}