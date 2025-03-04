using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Scenarios;

namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishAction : ScenarioAction<MqttNetworkSimulation, MqttPublishOptions>
    {
        public override async Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            var mqttClient = Context!.NetworkGraph!.GetNode(Config.MqttClientName, NodeType.Client) as IMqttClient;

            await mqttClient!.PublishAsync(Config.Topic, new byte[] { 7 }, MqttQos.AtLeastOnce);
        }
    }
}
