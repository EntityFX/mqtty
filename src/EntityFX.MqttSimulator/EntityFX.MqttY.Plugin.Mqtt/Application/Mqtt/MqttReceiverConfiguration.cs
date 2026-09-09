namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{
    public class MqttReceiverConfiguration
    {
        public string Server { get; set; } = string.Empty;

        public string[] Topics { get; set; } = new string[0];

        public EntityFX.MqttY.Contracts.Mqtt.MqttQos Qos { get; set; }
            = EntityFX.MqttY.Contracts.Mqtt.MqttQos.AtLeastOnce;
    }
}
