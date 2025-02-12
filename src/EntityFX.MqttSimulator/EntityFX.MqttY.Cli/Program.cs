using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Sources.Clear();

IHostEnvironment env = builder.Environment;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

builder.Services
    .Configure<NetworkGraphOptions>(
        builder.Configuration.GetSection("networkGraph"));

builder.Services
    .AddHostedService<Worker>()
    .AddTransient<IMonitoring, Monitoring>((sb) =>
    {
        var monitoring = new Monitoring();
        monitoring.Added += (sender, e) =>
            Console.WriteLine($"<{e.Date:u}>, {{{e.Type}}} {e.SourceType}[\"{e.From}\"] -> {e.DestinationType}[\"{e.To}\"]" +
                $"{(e.PacketSize > 0 ? $", Packet Size = {e.PacketSize}" : "")}" +
                $"{(!string.IsNullOrEmpty(e.Category) ? $", Category = {e.Category}" : "")}.");
        return monitoring;
    })
    .AddTransient<IPathFinder, DijkstraPathFinder>()
    .AddTransient<INetworkGraph, NetworkGraph>();


using IHost host = builder.Build();


await host.RunAsync();