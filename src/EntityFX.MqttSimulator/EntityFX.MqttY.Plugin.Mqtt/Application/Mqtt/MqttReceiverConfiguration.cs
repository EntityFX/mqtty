namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{
    public class MqttReceiverConfiguration
    {
        public string Server { get; set; } = string.Empty;

        public string[] Topics { get; set; } = new string[0];
    }
}
