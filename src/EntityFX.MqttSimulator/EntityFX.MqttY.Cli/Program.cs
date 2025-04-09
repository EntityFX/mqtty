using EntityFX.MqttY;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Sources.Clear();

IHostEnvironment env = builder.Environment;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.nodes.json", true, false)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, false);

builder.Services
    .Configure<MqttYOptions>(builder.Configuration);

builder.Services
    .AddHostedService<Worker>()
    .ConfigureServices();

using IHost host = builder.Build();


await host.RunAsync();