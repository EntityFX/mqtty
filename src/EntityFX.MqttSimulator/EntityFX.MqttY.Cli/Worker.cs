using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;

internal class Worker : BackgroundService
{
    private readonly IOptions<NetworkGraphOptions> options;
    private readonly INetworkGraph networkGraph;

    public Worker(IOptions<NetworkGraphOptions> options, INetworkGraph networkGraph)
    {
        this.options = options;
        this.networkGraph = networkGraph;
    }

    

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        networkGraph.Configure(this.options.Value);

        await base.StartAsync(cancellationToken);
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqttClient1 = networkGraph.GetNode("mc1", NodeType.Client) as IMqttClient;
        var mqttClient2 = networkGraph.GetNode("mc2", NodeType.Client) as IMqttClient;

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