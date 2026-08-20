namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    /// <summary>
    /// Предоставляет эталонные профили брокеров по их типу.
    /// </summary>
    public interface IBrokerBenchmarkRepository
    {
        MqttBrokerProfile? Get(string brokerType);
    }
}