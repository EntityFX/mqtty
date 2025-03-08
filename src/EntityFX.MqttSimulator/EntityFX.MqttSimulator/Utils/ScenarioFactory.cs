using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Utils;

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
            var actionsDictionary = BuildActions<NetworkSimulation>(options.Scenario, scenarioOptions);

            var scenario = new Scenario<NetworkSimulation>(_serviceProvider, new NetworkSimulation(),
                actionsDictionary.ToImmutableDictionary());
            return scenario;
        }

        return null;
    }

    private Dictionary<int, IAction<TContext>> BuildActions<TContext>(string scenarioName,
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

            IAction<TContext>? action = null;
            switch (actionOption.Value.Type)
            {
                case "network-init":
                    var networkGraphOption = configurationSection.Get<NetworkGraphOption>();

                    var networkGraph = _serviceProvider.GetRequiredService<INetworkGraph>();
                    networkGraph.OptionsPath = configurationPath;

                    var networkInitAction = new NetworkInitAction(networkGraph)
                    {
                        Config = networkGraphOption,
                        Delay = actionOptionValue.Delay,
                        Iterations = actionOptionValue.Iterations,
                        IterationsTimeout = actionOptionValue.IterationsTimeout,
                        ActionTimeout = actionOptionValue.ActionTimeout,
                        Index = actionOptionValue.Index
                    };
                    action = (IAction<TContext>)networkInitAction;
                    break;
                case "mqtt-publish":
                    var mqttPublishOptions = configurationSection.Get<MqttPublishOptions>();

                    var mqttPublishAction = new MqttPublishAction()
                    {
                        Config = mqttPublishOptions,
                        Delay = actionOptionValue.Delay,
                        Iterations = actionOptionValue.Iterations,
                        IterationsTimeout = actionOptionValue.IterationsTimeout,
                        ActionTimeout = actionOptionValue.ActionTimeout,
                        Index = actionOption.Value.Index
                    };
                    action = (IAction<TContext>)mqttPublishAction;
                    break;
            }

            if (action == null) continue;
            
            actions.Add(action);
        }

        var actionsDictionary = actions.ToDictionary(a => a.Index, a => a);
        return actionsDictionary;
    }

    public IScenario? Configure((string Scenario, IDictionary<string, ScenarioOption> Options) options, IScenario? service)
    {
        return service;
    }
}