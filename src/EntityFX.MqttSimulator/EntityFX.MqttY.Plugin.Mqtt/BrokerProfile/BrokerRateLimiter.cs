namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>A fractional token bucket with bounded accumulation and an initially empty balance.</summary>
    internal sealed class BrokerRateLimiter
    {
        private double _tokensPerTick;
        private long _lastRefillTick = -1;
        private double _tokens;
        public double BurstTokens { get; private set; }

        public BrokerRateLimiter(double maxRps, TimeSpan tickPeriod)
        {
            Update(maxRps, tickPeriod);
        }

        public void Update(double maxRps, TimeSpan tickPeriod)
        {
            if (!double.IsFinite(maxRps) || maxRps <= 0) throw new ArgumentOutOfRangeException(nameof(maxRps));
            if (tickPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(tickPeriod));
            _tokensPerTick = maxRps * tickPeriod.TotalSeconds;
            if (!double.IsFinite(_tokensPerTick)) throw new ArgumentOutOfRangeException(nameof(maxRps));
            BurstTokens = Math.Max(1.0, _tokensPerTick);
            _lastRefillTick = -1;
            _tokens = 0;
        }

        public bool TryAcquire(long currentTick)
        {
            if (currentTick < 0 || currentTick < _lastRefillTick)
                throw new ArgumentOutOfRangeException(nameof(currentTick));
            if (_lastRefillTick < 0) _lastRefillTick = currentTick;
            _tokens = Math.Min(BurstTokens, _tokens + (currentTick - _lastRefillTick) * _tokensPerTick);
            _lastRefillTick = currentTick;
            // A tiny tolerance avoids losing an acquisition to binary rounding at a whole token.
            if (_tokens + 1e-12 < 1.0) return false;
            _tokens = Math.Max(0, _tokens - 1.0);
            return true;
        }
    }
}
