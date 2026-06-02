using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Designer.Services;
using EntityFX.MqttY.Designer.ViewModels;
using EntityFX.MqttY.Designer.Views;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Designer;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            var serviceProvider = services.BuildServiceProvider();

            desktop.MainWindow = new MainWindow
            {
                DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration (empty for designer - no appsettings.json)
        services.AddSingleton<IConfiguration>(_ =>
            new ConfigurationBuilder().Build());

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<NetworkEditorViewModel>();
        services.AddSingleton<SimulationViewModel>();

        // Services
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<IGraphLayoutService, GraphLayoutService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<SimulationService>();
        services.AddSingleton<GraphMLImporterService>();
        services.AddSingleton<FileLogService>();

        // MqttY Core registrations
        services.ConfigureServices();
        services.ConfigureMqttServices();

        // Register INodesBuilder with all protocol factories
        services.AddScoped<INodesBuilder>(sp =>
        {
            var networkFactory = sp.GetRequiredService<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>>();
            return new NodesBuilder(
                new Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>>
                {
                    ["net"] = new ClientFactory(),
                    ["mqtt"] = new MqttClientFactory(),
                },
                new Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>>
                {
                    ["net"] = new ServerFactory(sp),
                    ["mqtt"] = new MqttServerFactory(sp),
                },
                new Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>>
                {
                    ["net"] = new ApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
                    ["mqtt"] = new MqttApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
                },
                (IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>>)networkFactory);
        });
    }
}