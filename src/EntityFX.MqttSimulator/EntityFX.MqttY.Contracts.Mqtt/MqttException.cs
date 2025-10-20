namespace EntityFX.MqttY.Contracts.Mqtt
{
    public class MqttException : Exception
    {
        public MqttException(string message, Exception? innerException = null) : base(message, innerException)
        {
        }
    }
}
