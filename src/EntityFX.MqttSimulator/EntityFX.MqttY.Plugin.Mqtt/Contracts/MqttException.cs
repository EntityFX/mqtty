namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public class MqttException : Exception
    {
        public MqttException(string message, Exception? innerException = null) : base(message, innerException)
        {
        }
    }
}
