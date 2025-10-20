using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Scenarios.Helper;
using EntityFX.MqttY.Scenarios;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Scenarios;

public class GeneratePlantUmlAction : ScenarioAction<NetworkSimulation, PathOptions>
{
    public override async Task ExecuteAsync()
    {
        if (Config == null)
        {
            throw new ArgumentNullException(nameof(Config));
        }

        var plantUmlGraphGenerator = ServiceProvider!.GetRequiredService<IUmlGraphGenerator>();

        var uml = plantUmlGraphGenerator.Generate(Context!.NetworkGraph!);

        var path = FileExtensions.ReplaceParams(Config.Path, Scenario!.Name!);
        var umlPath = Path.Combine(path, "network.puml");
        FileExtensions.CreateDirectory(path);

        await File.WriteAllTextAsync(umlPath, uml);
    }
}