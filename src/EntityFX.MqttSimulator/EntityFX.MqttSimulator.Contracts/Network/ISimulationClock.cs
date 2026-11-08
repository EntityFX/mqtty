namespace EntityFX.MqttY.Contracts.Network
{
    /// <summary>
    /// The simulation's model clock. It owns the tick counter and the per-worker
    /// step deltas; the refresh pipeline snapshots the step total once per tick so
    /// node counters do not rescan thread-local values.
    /// </summary>
    public interface ISimulationClock
    {
        long TotalTicks { get; }

        long TotalSteps { get; }

        /// <summary>Step total captured by the most recent <see cref="BeginRefresh"/>.</summary>
        long CurrentRefreshSteps { get; }

        /// <summary>Snapshots <see cref="TotalSteps"/> for the tick that is about to run.</summary>
        void BeginRefresh();

        void Tick();

        void Step();
    }
}
