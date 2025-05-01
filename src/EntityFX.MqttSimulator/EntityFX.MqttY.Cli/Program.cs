using EntityFX.MqttY;
using EntityFX.MqttY.Cli;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Mqtt;
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
    .AddHostedService<Worker>()
    .AddScoped<IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)>,
        ScenarioFactory>()
    .Configure<ScenariosOptions>(builder.Configuration)
    .ConfigureNodesBuilder()
    .ConfigureServices()
    .ConfigureMqttServices();

using IHost host = builder.Build();


await host.RunAsync();