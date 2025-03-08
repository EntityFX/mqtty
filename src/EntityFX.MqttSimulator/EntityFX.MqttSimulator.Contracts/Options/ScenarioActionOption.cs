namespace EntityFX.MqttY.Contracts.Options;

public class ScenarioActionOption
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;

    public TimeSpan? Delay { get; set; }
        
    public TimeSpan? IterationsTimeout { get; set; }
    
    public TimeSpan? ActionTimeout { get; set; }

    public int Iterations { get; set; } = 1;
}