using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Scenarios;
using EntityFX.MqttY.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Cli;

internal class ScenarioFactory : IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public ScenarioFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public IScenario? Create((string Scenario, IDictionary<string, ScenarioOption> Options) options)
    {
        if (string.IsNullOrEmpty(options.Scenario))
        {
            return null;
        }

        if (!options.Options.ContainsKey(options.Scenario))
        {
            return null;
        }

        var scenarioOptions = options.Options[options.Scenario];

        if (scenarioOptions.Type == "networkSimulation")
        {
            var scenario = new Scenario<NetworkSimulation>(_serviceProvider, options.Scenario, new NetworkSimulation(), 
                (s) => BuildActions<NetworkSimulation>(s, options.Scenario, scenarioOptions));
            return scenario;
        }

        return null;
    }

    private Dictionary<int, IAction<TContext>> BuildActions<TContext>(IScenario<NetworkSimulation> s,
        string scenarioName,
        ScenarioOption scenarioOptions)
    {
        var actions = new List<IAction<TContext>>();
        foreach (var actionOption
                 in scenarioOptions.Actions.OrderBy(a => a.Value.Index))
        {
            var configurationPath = $"scenarios:{scenarioName}:actions:{actionOption.Key}:configuration";

            var configurationSection =
                _configuration.GetSection(configurationPath);

            var actionOptionValue = actionOption.Value;

            var action = BuildAction<TContext>(s, actionOption, configurationPath, configurationSection, actionOptionValue);

            if (action == null) continue;

            actions.Add(action);
        }

        var actionsDictionary = actions.ToDictionary(a => a.Index, a => a);
        return actionsDictionary;
    }

    private IAction<TContext>? BuildAction<TContext>(IScenario<NetworkSimulation> s, 
        KeyValuePair<string, ScenarioActionOption> actionOption, string configurationPath, 
        IConfigurationSection configurationSection, ScenarioActionOption actionOptionValue)
    {
        switch (actionOption.Value.Type)
        {
            case "network-init":
                var networkGraphOption = configurationSection.GetSection("graph").Get<NetworkGraphOption>();
                var monitoringOption = configurationSection.GetSection("monitoring").Get<MonitoringOption>();

                var networkGraph = _serviceProvider.GetRequiredService<IFactory<INetworkSimulator, NetworkGraphFactoryOption>>();
                var networkSimulatorBuilder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();

                var config = new NetworkGraphFactoryOption()
                {
                    MonitoringOption = monitoringOption ?? new MonitoringOption(),
                    NetworkGraphOption = networkGraphOption ?? new NetworkGraphOption(),
                    OptionsPath = configurationPath,
                    NetworkGraphFactory = networkGraph,
                    NetworkSimulatorBuilder = networkSimulatorBuilder
                };
                return (IAction<TContext>)BuildAction<NetworkSimulation, NetworkInitAction, NetworkGraphFactoryOption>(
                    s, actionOption, configurationSection, actionOptionValue, config);
            case "mqtt-publish":
                return (IAction<TContext>)BuildAction<NetworkSimulation, MqttPublishAction, MqttPublishOptions>(
                    s, actionOption, configurationSection, actionOptionValue);
            case "wait-network-queue":
                return (IAction<TContext>)BuildAction<NetworkSimulation, WaitNetwokQueueEmptyAction, WaitNetwokQueueEmptyOptions>(
                    s, actionOption, configurationSection, actionOptionValue);
        }

        return null;
    }

    private static TAction BuildAction<TContext, TAction, TConfig>(
        IScenario<TContext> s, KeyValuePair<string, ScenarioActionOption> actionOption, 
        IConfigurationSection configurationSection, ScenarioActionOption actionOptionValue, TConfig? initConfig = default)
        where TAction : IAction<TContext, TConfig>, new()
        where TConfig : new()
    {
        var waitNetwokQueueEmptyOptions = (initConfig != null ? initConfig : configurationSection.Get<TConfig>()) ?? new TConfig();
        var waitNetwokQueueEmptyAction = new TAction()
        {
            Config = waitNetwokQueueEmptyOptions,
            Delay = actionOptionValue.Delay,
            Iterations = actionOptionValue.Iterations,
            IterationsTimeout = actionOptionValue.IterationsTimeout,
            ActionTimeout = actionOptionValue.ActionTimeout,
            Index = actionOption.Value.Index,
            Scenario = s
        };

        return waitNetwokQueueEmptyAction;
    }

    public IScenario? Configure((string Scenario, IDictionary<string, ScenarioOption> Options) options, IScenario? service)
    {
        return service;
    }
}