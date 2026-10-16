namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>A fractional token bucket with an initially empty balance and a bounded burst.
    /// One tick of capacity plus one token preserves fractional credit across tick batches.
    /// </summary>
    internal sealed class BrokerRateLimiter
    {
        private double _tokensPerTick;
        private long _lastRefillTick;
        private double _tokens;
        private double _nextReservationTick;
        private long _lastReservationTick;
        public double BurstTokens { get; private set; }

        public BrokerRateLimiter(double maxRps, TimeSpan tickPeriod)
        {
            Update(maxRps, tickPeriod);
        }

        public void Update(double maxRps, TimeSpan tickPeriod)
        {
            lock (this)
            {
                if (!double.IsFinite(maxRps) || maxRps <= 0) throw new ArgumentOutOfRangeException(nameof(maxRps));
                if (tickPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(tickPeriod));
                _tokensPerTick = maxRps * tickPeriod.TotalSeconds;
                if (!double.IsFinite(_tokensPerTick)) throw new ArgumentOutOfRangeException(nameof(maxRps));
                // A fractional remainder can be just below one token when the next
                // tick's budget arrives. Capping at one tick alone discards that
                // remainder and rounds sustained throughput down to whole tokens/tick.
                BurstTokens = _tokensPerTick + 1.0;
                _lastRefillTick = 0;
                _tokens = 0;
                _nextReservationTick = 0;
                _lastReservationTick = 0;
            }
        }

        public bool TryAcquire(long currentTick)
        {
            lock (this)
            {
                if (currentTick < 0 || currentTick < _lastRefillTick)
                    throw new ArgumentOutOfRangeException(nameof(currentTick));
                _tokens = Math.Min(BurstTokens, _tokens + (currentTick - _lastRefillTick) * _tokensPerTick);
                _lastRefillTick = currentTick;
                // Repeated fractional refills accumulate binary rounding error.
                // One billionth of a token avoids rejecting a whole-token boundary
                // after long runs without granting a meaningful extra rate budget.
                if (_tokens + 1e-9 < 1.0) return false;
                _tokens = Math.Max(0, _tokens - 1.0);
                return true;
            }
        }

        /// <summary>FIFO capacity pacing: defer work instead of inventing a failed publish.
        /// Fractional eligibility is retained, so sub-tick rates do not round down.</summary>
        public int Reserve(long currentTick)
        {
            lock (this)
            {
                if (currentTick < 0 || currentTick < _lastReservationTick)
                    throw new ArgumentOutOfRangeException(nameof(currentTick));
                _lastReservationTick = currentTick;
                var eligible = Math.Max(currentTick, _nextReservationTick);
                var wait = Math.Ceiling(Math.Max(0, eligible - currentTick) - 1e-9);
                if (!double.IsFinite(wait) || wait > int.MaxValue)
                    throw new InvalidDataException("Capacity backlog cannot be represented in ticks.");
                _nextReservationTick = eligible + 1 / _tokensPerTick;
                return checked((int)Math.Max(0, wait));
            }
        }
    }
}
