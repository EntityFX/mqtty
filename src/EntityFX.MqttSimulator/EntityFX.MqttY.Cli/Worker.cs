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
        var client = networkGraph.GetNode("mc1", NodeType.Client) as IClient;

        if (client == null)
        {
            await Task.CompletedTask;
        }

        IMqttClient? mqttClient = null;
        if (client?.ProtocolType == "mqtt")
        {
            mqttClient = client as IMqttClient;

            await mqttClient!.ConnectAsync(mqttClient.Server, MqttQos.AtLeastOnce, true);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await mqttClient!.PublishAsync("topic/one", new byte[] { 1 }, MqttQos.AtMostOnce);
            await Task.Delay(2000, stoppingToken);
        }
        await Task.CompletedTask;
    }
}