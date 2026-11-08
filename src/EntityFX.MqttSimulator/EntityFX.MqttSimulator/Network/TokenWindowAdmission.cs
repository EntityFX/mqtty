using System.Collections.Concurrent;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Rolling byte-window admission: every admitted packet contributes its bytes
/// to a budget for <c>windowTicks</c>. Claims use one interlocked add, so sends
/// from many nodes on the same network do not serialise on a monitor.
/// </summary>
public sealed class TokenWindowAdmission : IAdmissionPolicy
{
    private readonly ConcurrentQueue<(long Tick, int Bytes)> _admissions = new();
    private readonly double _budgetBytes;
    private readonly int _windowTicks;
    private long _windowBytes;

    public TokenWindowAdmission(double budgetBytes, int windowTicks)
    {
        _budgetBytes = budgetBytes;
        _windowTicks = windowTicks;
    }

    public long WindowBytes => Volatile.Read(ref _windowBytes);

    public bool TryReserve(long tick, int bytes)
    {
        // The window is pruned once per Refresh, but sends can also be driven by
        // plain Tick() (and by tests) without a refresh. Prune lazily only when
        // the new claim may exceed the budget.
        if (Volatile.Read(ref _windowBytes) + bytes > _budgetBytes)
        {
            Prune(tick);
        }

        var windowBytes = Interlocked.Add(ref _windowBytes, bytes);
        if (windowBytes > _budgetBytes)
        {
            Interlocked.Add(ref _windowBytes, -bytes);
            return false;
        }

        return true;
    }

    public void Commit(long tick, int bytes) => _admissions.Enqueue((tick, bytes));

    public void Rollback(int bytes) => Interlocked.Add(ref _windowBytes, -bytes);

    public void Prune(long tick)
    {
        while (_admissions.TryPeek(out var admission) &&
               tick - admission.Tick >= _windowTicks)
        {
            if (_admissions.TryDequeue(out var expired))
            {
                Interlocked.Add(ref _windowBytes, -expired.Bytes);
            }
        }
    }

    public void Reset()
    {
        _admissions.Clear();
        Interlocked.Exchange(ref _windowBytes, 0);
    }
}
