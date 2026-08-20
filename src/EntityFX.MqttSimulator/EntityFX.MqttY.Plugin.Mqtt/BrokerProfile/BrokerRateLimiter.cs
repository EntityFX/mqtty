namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>
    /// Дробный токен-бакет без накопительного дрейфа. Доступные токены вычисляются
    /// от абсолютного стартового тика, что исключает накопление ошибки округления.
    /// Позволяет воспроизводить RPS как ниже, так и выше частоты тиков (при
    /// tickPeriod = 0.1 мс частота тиков 10 000/с, тогда как бенчмарк даёт RPS до ~82 000/с).
    /// </summary>
    internal sealed class BrokerRateLimiter
    {
        private double _tokensPerTick;
        private double _burstTokens;
        private long _startTick = -1;
        private double _consumed;

        public BrokerRateLimiter(double maxRps, TimeSpan tickPeriod)
        {
            Update(maxRps, tickPeriod);
        }

        public void Update(double maxRps, TimeSpan tickPeriod)
        {
            _tokensPerTick = maxRps > 0 && tickPeriod > TimeSpan.Zero
                ? maxRps * tickPeriod.TotalSeconds
                : 1.0;

            _burstTokens = Math.Max(1.0, _tokensPerTick);
            _startTick = -1;
            _consumed = 0.0;
        }

        public bool TryAcquire(long currentTick)
        {
            if (_startTick < 0)
            {
                _startTick = currentTick;
            }

            var elapsed = currentTick - _startTick;
            var available = Math.Min(_burstTokens, elapsed * _tokensPerTick - _consumed);

            if (available >= 1.0)
            {
                _consumed += 1.0;
                return true;
            }

            return false;
        }
    }
}