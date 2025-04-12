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
        private readonly INetworkSimulatorBuilder networkSimulatorBuilder;
        private readonly IFactory<INetworkSimulator, NetworkGraphFactoryOption> networkGraphFactory;

        public NetworkInitAction(INetworkSimulatorBuilder networkSimulatorBuilder, IScenario<NetworkSimulation> scenario, 
            IFactory<INetworkSimulator, NetworkGraphFactoryOption> networkGraphFactory)
            : base(scenario)
        {
            this.networkSimulatorBuilder = networkSimulatorBuilder;
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

            Context!.NetworkGraph = networkGraphFactory.Create(Config);

            Context!.NetworkGraph!.StartPeriodicRefreshAsync();

            networkSimulatorBuilder.Configure(Context!.NetworkGraph, Config.NetworkGraphOption);

            return Task.CompletedTask;
        }

        protected override void Finish()
        {
            Context!.NetworkGraph!.StopPeriodicRefresh();
        }
    }
}
