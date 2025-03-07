using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishAction : ScenarioAction<NetworkSimulation, MqttPublishOptions>
    {
        public override async Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            var mqttClient = Context!.NetworkGraph!.GetNode(Config.MqttClientName, NodeType.Client) as IMqttClient;

            await mqttClient!.PublishAsync(Config.Topic, new byte[] { 7 }, MqttQos.AtLeastOnce);

            Context!.NetworkGraph!.Refresh();
        }
    }
}
