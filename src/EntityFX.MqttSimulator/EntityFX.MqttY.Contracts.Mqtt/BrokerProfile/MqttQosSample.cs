namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    /// <summary>
    /// Опорная точка эталонного профиля брокера для одного уровня QoS.
    /// </summary>
    /// <param name="Clients">Число подключённых клиентов.</param>
    /// <param name="Rps">Пропускная способность, сообщений в секунду.</param>
    /// <param name="LatencyMs">99-й перцентиль задержки, мс.</param>
    /// <param name="FailRate">Доля отказов в диапазоне [0, 1].</param>
    public readonly record struct MqttQosSample(
        int Clients,
        double Rps,
        double LatencyMs,
        double FailRate);
}