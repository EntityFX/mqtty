using System.Threading;

namespace EntityFX.MqttY.Counter
{
    /// <summary>
    /// Global simulation clock shared by all counters. Instead of writing
    /// LastTicks/LastSteps into every counter instance on every tick (a full
    /// tree walk per node per tick), the simulator publishes the current tick
    /// once and counters read it on demand.
    /// </summary>
    internal static class CounterClock
    {
        private static long _ticks;
        private static long _steps;

        public static long Ticks => Volatile.Read(ref _ticks);

        public static long Steps => Volatile.Read(ref _steps);

        public static void Publish(long ticks, long steps)
        {
            Volatile.Write(ref _ticks, ticks);
            Volatile.Write(ref _steps, steps);
        }
    }
}
