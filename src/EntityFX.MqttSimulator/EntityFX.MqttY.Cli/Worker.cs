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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _networkGraph.Configure(this._options.Value);
        var plantGraph = _plantUmlGraphGenerator.Generate(_networkGraph);
        File.WriteAllText("graph.puml", plantGraph);

        await Task.Delay(1000);


        var mqttClient1 = _networkGraph.GetNode("mc1", NodeType.Client) as IMqttClient;
        var mqttClient2 = _networkGraph.GetNode("mc2", NodeType.Client) as IMqttClient;


        await Task.Delay(1000);

        await mqttClient1!.PublishAsync("telemetry/temperature", new byte[] { 7 }, MqttQos.AtLeastOnce);


        while (!stoppingToken.IsCancellationRequested)
        {
            await mqttClient1!.PublishAsync("telemetry/temperature", new byte[] { 7 }, MqttQos.AtLeastOnce);
            await Task.Delay(2000);

            _networkGraph.Refresh();
        }
        await Task.CompletedTask;
    }
}