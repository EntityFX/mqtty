using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public class MqttConnectException : MqttException
    {
        private readonly MqttConnectionStatus _mqttConnectionStatus;

        public MqttConnectException(MqttConnectionStatus mqttConnectionStatus, string message)
            : base(message)
        {
            this._mqttConnectionStatus = mqttConnectionStatus;
        }
    }
}
