using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using EntityFX.MqttY.Utils;

internal class Worker : BackgroundService
{
    private readonly IOptions<NetworkGraphOptions> _options;
    private readonly INetworkGraph _networkGraph;
    private readonly PlantUmlGraphGenerator _plantUmlGraphGenerator;

    public Worker(IOptions<NetworkGraphOptions> options, INetworkGraph networkGraph, 
        PlantUmlGraphGenerator plantUmlGraphGenerator)
    {
        this._options = options;
        this._networkGraph = networkGraph;
        _plantUmlGraphGenerator = plantUmlGraphGenerator;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _networkGraph.Configure(this._options.Value);
        var plantGraph = _plantUmlGraphGenerator.Generate(_networkGraph);
        File.WriteAllText("graph.puml", plantGraph);

        await base.StartAsync(cancellationToken);
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqttClient1 = _networkGraph.GetNode("mc1", NodeType.Client) as IMqttClient;
        var mqttClient2 = _networkGraph.GetNode("mc2", NodeType.Client) as IMqttClient;

        if (mqttClient1 == null)
        {
            await Task.CompletedTask;
        }

        if (mqttClient2 == null)
        {
            await Task.CompletedTask;
        }

        await mqttClient1!.ConnectAsync(mqttClient1.Server, MqttQos.AtLeastOnce, true);
        await mqttClient2!.ConnectAsync(mqttClient1.Server, MqttQos.AtLeastOnce, true);


        await mqttClient2!.SubscribeAsync(mqttClient1.Server, MqttQos.AtLeastOnce);


        while (!stoppingToken.IsCancellationRequested)
        {
            await mqttClient1!.PublishAsync("topic/one", new byte[] { 1 }, MqttQos.AtMostOnce);
            await Task.Delay(2000, stoppingToken);
        }
        await Task.CompletedTask;
    }
}