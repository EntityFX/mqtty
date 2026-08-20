namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>
    /// Интервальный регулятор пропускной способности (токен-бакет с интервалом
    /// поступления, выраженным в тиках). Ограничивает максимальный RPS брокера
    /// для одного уровня QoS.
    /// </summary>
    internal sealed class BrokerRateLimiter
    {
        private long _ticksPerToken = 1;
        private long _lastAcquireTick = -1;

        public BrokerRateLimiter(double maxRps, TimeSpan tickPeriod)
        {
            Update(maxRps, tickPeriod);
        }

        public void Update(double maxRps, TimeSpan tickPeriod)
        {
            if (maxRps <= 0 || tickPeriod <= TimeSpan.Zero)
            {
                _ticksPerToken = 1;
                return;
            }

            var ticksPerSecond = 1.0 / tickPeriod.TotalSeconds;
            _ticksPerToken = Math.Max(1, (long)Math.Floor(ticksPerSecond / maxRps));
        }

        public bool TryAcquire(long currentTick)
        {
            if (_lastAcquireTick < 0)
            {
                _lastAcquireTick = currentTick;
                return true;
            }

            if (currentTick - _lastAcquireTick >= _ticksPerToken)
            {
                _lastAcquireTick = currentTick;
                return true;
            }

            return false;
        }
    }
}