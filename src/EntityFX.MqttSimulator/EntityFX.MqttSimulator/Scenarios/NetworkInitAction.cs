using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Scenarios
{
    internal class NetworkInitAction : ScenarioAction<NetworkSimulation, NetworkGraphFactoryOption>
    {
        private readonly IFactory<INetworkGraph, NetworkGraphFactoryOption> networkGraphFactory;

        public NetworkInitAction(IScenario<NetworkSimulation> scenario, 
            IFactory<INetworkGraph, NetworkGraphFactoryOption> networkGraphFactory)
            : base(scenario)
        {
            this.networkGraphFactory = networkGraphFactory;
        }

        public override Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            if (Config.MonitoringOption.Path != null)
            {
                Config.MonitoringOption.Path = Config.MonitoringOption.Path
                    .Replace("{scenario}", Scenario.Name)
                    .Replace("{date}", $"{DateTime.Now:yyyy_MM_dd__HH_mm}");
            }

            var networkGraph = networkGraphFactory.Create(Config);

            networkGraph.Configure(Config.NetworkGraphOption);

            Context!.NetworkGraph = networkGraph;

            networkGraph.Refresh();

            return Task.CompletedTask;
        }
    }
}
