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
        var client = networkGraph.GetNode("c1", NodeType.Client) as IClient;
        while (!stoppingToken.IsCancellationRequested)
        {
            await client.SendAsync(new byte[] { 1 });
            await Task.Delay(2000, stoppingToken);
        }
        await Task.CompletedTask;
    }
}