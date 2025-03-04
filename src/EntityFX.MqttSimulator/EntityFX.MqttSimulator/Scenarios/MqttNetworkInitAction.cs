using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Scenarios
{
    public class MqttNetworkInitAction : ScenarioAction<MqttNetworkSimulation, NetworkGraphOptions>
    {
        private readonly NetworkGraphOptions options;
        private readonly INetworkGraph networkGraph;

        public MqttNetworkInitAction(NetworkGraphOptions options, INetworkGraph networkGraph)
        {
            this.options = options;
            this.networkGraph = networkGraph;
        }

        public override Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            networkGraph.Configure(Config);

            Context!.NetworkGraph = networkGraph;

            return Task.CompletedTask;
        }
    }
}
