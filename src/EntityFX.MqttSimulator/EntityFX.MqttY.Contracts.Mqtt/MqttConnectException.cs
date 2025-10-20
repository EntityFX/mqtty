using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public class MqttConnectException : MqttException
    {
        private readonly MqttConnectionStatus _mqttConnectionStatus;

        public MqttConnectException(MqttConnectionStatus mqttConnectionStatus, string message)
            : base(message)
        {
            _mqttConnectionStatus = mqttConnectionStatus;
        }
    }
}
