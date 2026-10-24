using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Engines.Federation;
using EntityFX.MqttY.Engines.Hosting;
using EntityFX.MqttY.Engines.LoadBalancing;
using EntityFX.MqttY.Engines.Relay;
using EntityFX.MqttY.Web.App.Hubs;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

if (args.Length == 2 && args[0] == "--execute-simulation")
{
    Environment.ExitCode = await SimulationWorker.ExecuteAsync(args[1]);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

// Настройки
builder.Services.Configure<DataDirectoryOptions>(builder.Configuration.GetSection("MqttYWeb"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<DataDirectoryOptions>>().Value;
    options.MaxParallelJobLimit = Math.Clamp(options.MaxParallelJobs, 1, 8);
    options.MaxParallelJobs = options.MaxParallelJobLimit;
    return options;
});
builder.Services.AddSingleton(sp => new JsonFileStore(sp.GetRequiredService<DataDirectoryOptions>().DataDirectory));

// Движки
builder.Services.AddSingleton<NetworkSimulationEngine>();
builder.Services.AddSingleton<RelayEngine>();
builder.Services.AddSingleton<FederationEngine>();
builder.Services.AddSingleton<LoadBalancingEngine>();
builder.Services.AddSingleton<EngineHost>(sp => new EngineHost(new IExperimentEngine[]
{
    sp.GetRequiredService<RelayEngine>(),
    sp.GetRequiredService<FederationEngine>(),
    sp.GetRequiredService<LoadBalancingEngine>()
}));

// Сервисы
builder.Services.AddSingleton<IPresetCatalogService, PresetCatalogService>();
builder.Services.AddSingleton<ITopologyGeneratorService, TopologyGeneratorService>();
builder.Services.AddSingleton<IConfigurationMapperService, ConfigurationMapperService>();
builder.Services.AddSingleton<NativeConfigurationService>();
builder.Services.AddSingleton<IJobRegistry, JobRegistry>();
builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
builder.Services.AddSingleton<InProcessJobExecutor>();
builder.Services.AddSingleton<OutOfProcessJobExecutor>();
builder.Services.AddSingleton<ISimulationJobRunner, SimulationJobRunner>();
builder.Services.AddHostedService(sp => (SimulationJobRunner)sp.GetRequiredService<ISimulationJobRunner>());
builder.Services.AddHostedService<MonitoringHubForwarder>();

// SignalR
builder.Services.AddSignalR();

// REST + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MqttY.Web API",
        Version = "v1",
        Description = "REST API веб-приложения MqttY.Web: пресеты, топологии, симуляции, мониторинг."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("MqttYWeb:EnableSwagger"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/healthz", (IJobRegistry registry, JsonFileStore store) => new
{
    status = "ok", processId = Environment.ProcessId,
    processWorkingSetBytes = Environment.WorkingSet,
    dataDirectoryAvailable = Directory.Exists(store.Root),
    runningJobs = registry.GetAll().Count(j => j.State == "Running"),
    queuedJobs = registry.GetAll().Count(j => j.State is "Queued" or "Paused")
});
app.MapHub<MonitoringHub>("/hubs/monitoring");

app.Run();

/// <summary>Точка входа для интеграционных тестов (WebApplicationFactory).</summary>
public partial class Program
{
}
