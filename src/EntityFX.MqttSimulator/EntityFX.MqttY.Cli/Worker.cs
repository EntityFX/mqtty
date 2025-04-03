using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Configuration;
using EntityFX.MqttY.Contracts.Scenarios;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Scenarios;
using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Helper;

internal class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IOptions<MqttYOptions> _options;
    private readonly IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)> _scenariosFactory;
    private readonly PlantUmlGraphGenerator _plantUmlGraphGenerator;

    public Worker(IServiceProvider serviceProvider, IConfiguration configuration,
        IOptions<MqttYOptions> options, IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)> scenariosFactory,
        PlantUmlGraphGenerator plantUmlGraphGenerator)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _options = options;
        _scenariosFactory = scenariosFactory;
        _plantUmlGraphGenerator = plantUmlGraphGenerator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkSimulation? context = null;
        using (var scenario = _scenariosFactory.Create(
            new(_options.Value.StartScenario, _options.Value.Scenarios)))
        {
            if (scenario == null)
            {
                return;
            }

            await scenario.ExecuteAsync();

            context = scenario.Context as NetworkSimulation;

            Console.Write(context!.NetworkGraph!.Counters.Dump());
        }

         

        //var items = context?.NetworkGraph?.Monitoring.GetByFilter(new MonitoringFilter()
        //{
        //    ByMonitoringType = new[] { MonitoringType.Link }
        //});

        //var byProtocol = context?.NetworkGraph?.Monitoring.GetByFilter(new MonitoringFilter()
        //{
        //    ByProtocol = "mqtt"
        //});

        //var categoryCounters = context?.NetworkGraph?.Monitoring.GetCountersByCategory();

        //var plantGraph = _plantUmlGraphGenerator.Generate(_networkGraph);
        //File.WriteAllText("graph.puml", plantGraph);



        await Task.Delay(1000);


    }
}