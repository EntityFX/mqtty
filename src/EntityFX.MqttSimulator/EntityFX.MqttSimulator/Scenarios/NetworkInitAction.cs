using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Scenarios
{
    public class NetworkInitAction : ScenarioAction<NetworkSimulation, NetworkGraphOptions>
    {
        private readonly INetworkGraph networkGraph;

        public NetworkInitAction(INetworkGraph networkGraph)
        {
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

            networkGraph.Refresh();

            return Task.CompletedTask;
        }
    }
}
