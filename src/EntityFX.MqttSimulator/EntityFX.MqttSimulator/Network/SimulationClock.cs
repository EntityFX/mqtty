using System.Collections.Generic;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Default model clock: one tick counter and one step delta per worker thread.
/// Summing thread-local deltas avoids a shared cache line on every Send/Receive.
/// </summary>
public sealed class SimulationClock : ISimulationClock
{
    private long _tick;
    private long _refreshSteps;
    private readonly ThreadLocal<long> _stepDeltas = new(trackAllValues: true);

    public long TotalTicks => Volatile.Read(ref _tick);

    public long TotalSteps
    {
        get
        {
            long sum = 0;
            foreach (var delta in _stepDeltas.Values)
            {
                sum += delta;
            }
            return sum;
        }
    }

    public long CurrentRefreshSteps => Volatile.Read(ref _refreshSteps);

    public void BeginRefresh() => Volatile.Write(ref _refreshSteps, TotalSteps);

    public void Tick() => Interlocked.Increment(ref _tick);

    public void Step() => _stepDeltas.Value++;
}
