namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttReceiverConfiguration
    {
        public string Server { get; set; } = string.Empty;

        public string[] Topics { get; set; } = new string[0];
    }
}
