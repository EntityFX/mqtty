namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>
    /// Детерминированная модель вероятностного отказа с фиксированным seed.
    /// </summary>
    internal sealed class BrokerFailModel
    {
        private readonly Random _random;

        public BrokerFailModel(int seed)
        {
            _random = new Random(seed);
        }

        public bool ShouldFail(double probability)
        {
            return probability > 0.0 && _random.NextDouble() < probability;
        }
    }
}