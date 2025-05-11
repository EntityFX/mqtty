using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EntityFX.MqttY.Cli;

internal class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IOptions<ScenariosOptions> _options;
    private readonly IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)> _scenariosFactory;
    private readonly PlantUmlGraphGenerator _plantUmlGraphGenerator;

    public Worker(IServiceProvider serviceProvider, IConfiguration configuration,
        IHostApplicationLifetime hostApplicationLifetime,
        IOptions<ScenariosOptions> options, IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)> scenariosFactory,
        PlantUmlGraphGenerator plantUmlGraphGenerator)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _hostApplicationLifetime = hostApplicationLifetime;
        _options = options;
        _scenariosFactory = scenariosFactory;
        _plantUmlGraphGenerator = plantUmlGraphGenerator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
       
        using var scenario = _scenariosFactory.Create(
            new(_options.Value.StartScenario, _options.Value.Scenarios));
        if (scenario == null)
        {
            return;
        }

        await scenario.ExecuteAsync();

        Console.Write("Executed");
        _hostApplicationLifetime.StopApplication();
        await StopAsync(stoppingToken);

    }
}