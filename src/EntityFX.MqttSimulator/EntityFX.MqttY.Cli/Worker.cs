using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

internal class Worker : IHostedService
{
    private readonly IOptions<NetworkGraphOptions> options;
    private readonly INetworkGraph networkGraph;

    public Worker(IOptions<NetworkGraphOptions> options, INetworkGraph networkGraph)
    {
        this.options = options;
        this.networkGraph = networkGraph;
    }


    public Task StartAsync(CancellationToken cancellationToken)
    {
        networkGraph.Configure(this.options.Value);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}