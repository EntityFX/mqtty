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
using EntityFX.MqttY.Scenarios;

internal class Worker : BackgroundService
{
    private readonly IServiceProvider serviceProvider;
    private readonly IConfiguration configuration;
    private readonly IOptions<NetworkGraphOptions> _options;
    private readonly INetworkGraph _networkGraph;
    private readonly PlantUmlGraphGenerator _plantUmlGraphGenerator;

    public Worker(IServiceProvider serviceProvider, IConfiguration configuration, IOptions<NetworkGraphOptions> options, INetworkGraph networkGraph,
        PlantUmlGraphGenerator plantUmlGraphGenerator)
    {
        this.serviceProvider = serviceProvider;
        this.configuration = configuration;
        this._options = options;
        this._networkGraph = networkGraph;
        _plantUmlGraphGenerator = plantUmlGraphGenerator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {


   //     _networkGraph.Configure(this._options.Value);

        var actions = new Dictionary<int, IAction<NetworkSimulation>>()
        {
            [0] = new MqttNetworkInitAction(this._options.Value, _networkGraph)
            {
                Delay = TimeSpan.FromSeconds(1),
                Config = this._options.Value
            },
            [1] = new MqttPublishAction()
            {
                IterrationsTimeout = TimeSpan.FromSeconds(10),
                //Iterrations = 7,
                Config = new MqttPublishOptions()
                {
                    Payload = new byte[] { 9, 8, 7 },
                    Topic = "telemetry/temperature",
                    MqttClientName = "mgx1_1"
                }
            },
        };

        var worker = new Scenario<NetworkSimulation>(serviceProvider, new NetworkSimulation(), actions.ToImmutableDictionary());
        await worker.ExecuteAsync();

        //var plantGraph = _plantUmlGraphGenerator.Generate(_networkGraph);
        //File.WriteAllText("graph.puml", plantGraph);

        await Task.Delay(1000);


        //var mgx11 = _networkGraph.GetNode("mgx11", NodeType.Client) as IMqttClient;
        // var mqttClient2 = _networkGraph.GetNode("mc2", NodeType.Client) as IMqttClient;

        //await mgx11!.PublishAsync("telemetry/temperature", new byte[] { 7 }, MqttQos.AtLeastOnce);

    }
}