using System.Diagnostics;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Persistent worker threads for the bulk-synchronous simulation refresh.
///
/// The refresh happens every (very short) simulation tick, so scheduling a
/// fresh <see cref="Task"/> per tick and synchronously waiting for it costs more
/// than the tick's actual work. This pool keeps the workers alive across ticks
/// and uses a generation counter plus a two-phase barrier instead:
///
///   phase 1 (networks)  -> barrier -> phase 2 (nodes) -> barrier
///
/// The calling thread participates in both phases, so a machine with N logical
/// cores uses N workers total. Work is claimed dynamically through an atomic
/// index, which keeps skew between dense (client) and sparse (hop) networks
/// from serialising the refresh on one worker.
/// </summary>
internal sealed class TickWorkerPool : IDisposable
{
    // How long an idle worker spins before parking on the monitor. The gap
    // between ticks is normally only a few microseconds, so spinning avoids a
    // kernel wake-up on every single tick.
    private const int SpinIterations = 64;

    private static readonly bool BusyStatsEnabled =
        Environment.GetEnvironmentVariable("MQTTY_WORKER_STATS") == "1";

    private readonly int _workerCount;
    private readonly Thread[] _threads;
    private readonly int[] _workerGenerations;
    private readonly long[] _phase1Busy;
    private readonly long[] _phase2Busy;
    private readonly Barrier _phaseBarrier;
    private readonly object _gate = new();

    private volatile bool _stop;
    private int _generation;

    private Action<int>? _phase1;
    private Action<int>? _phase2;
    private int _count1;
    private int _count2;
    private int _index1;
    private int _index2;
    private int _chunkSize;
    private Exception? _error;

    public TickWorkerPool(int workerCount)
    {
        _workerCount = Math.Max(1, workerCount);
        _phaseBarrier = new Barrier(_workerCount);
        _phase1Busy = new long[_workerCount];
        _phase2Busy = new long[_workerCount];

        // The calling thread is worker 0; the background threads are 1..N-1.
        _threads = new Thread[_workerCount - 1];
        _workerGenerations = new int[_threads.Length];
        for (var i = 0; i < _threads.Length; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"mqtty-refresh-{i + 1}"
            };
            _threads[i] = thread;
            thread.Start(i);
        }
    }

    public int WorkerCount => _workerCount;

    /// <summary>Wall time of the caller's part of the last tick (diagnostics only).</summary>
    public long LastNetworkPhaseTicks { get; private set; }
    public long LastNodePhaseTicks { get; private set; }
    public long LastTickTicks { get; private set; }

    public long[] Phase1Busy => _phase1Busy;
    public long[] Phase2Busy => _phase2Busy;

    /// <summary>
    /// Runs both refresh phases. <paramref name="phase1"/> and
    /// <paramref name="phase2"/> are executed by all workers; the caller also
    /// participates. Any exception from a phase is rethrown on the caller after
    /// all workers reached the final barrier.
    /// </summary>
    public void Run(Action<int>? phase1, int count1, Action<int>? phase2, int count2)
    {
        Run(phase1, count1, phase2, count2, chunkSize: 1);
    }

    /// <summary>
    /// Runs both refresh phases with chunked work claiming. Each worker grabs
    /// <paramref name="chunkSize"/> items per Interlocked operation, reducing
    /// atomic contention when per-item work is tiny.
    /// </summary>
    public void Run(Action<int>? phase1, int count1, Action<int>? phase2, int count2,
        int chunkSize)
    {
        if (_threads.Length == 0)
        {
            var start = Stopwatch.GetTimestamp();
            _index1 = 0;
            _index2 = 0;
            RunPhaseChunked(phase1, count1, ref _index1, chunkSize);
            var networkEnd = Stopwatch.GetTimestamp();
            RunPhaseChunked(phase2, count2, ref _index2, chunkSize);
            var end = Stopwatch.GetTimestamp();
            LastNetworkPhaseTicks = networkEnd - start;
            LastNodePhaseTicks = end - networkEnd;
            LastTickTicks = end - start;
            return;
        }

        lock (_gate)
        {
            _phase1 = phase1;
            _phase2 = phase2;
            _count1 = count1;
            _count2 = count2;
            _index1 = 0;
            _index2 = 0;
            _error = null;
            _chunkSize = chunkSize;
            _generation++;
            Monitor.PulseAll(_gate);
        }

        ExecuteTick();

        var error = Volatile.Read(ref _error);
        if (error != null)
        {
            throw new AggregateException(error);
        }
    }

    private void WorkerLoop(object? state)
    {
        var worker = (int)state!;
        var lastGeneration = _workerGenerations[worker];

        while (true)
        {
            if (!WaitForNextGeneration(lastGeneration))
            {
                return;
            }

            Action<int>? phase1;
            Action<int>? phase2;
            int count1;
            int count2;

            lock (_gate)
            {
                if (_stop)
                {
                    return;
                }

                lastGeneration = _generation;
                phase1 = _phase1;
                phase2 = _phase2;
                count1 = _count1;
                count2 = _count2;
            }

            ExecuteTick(phase1, count1, phase2, count2, worker + 1);
        }
    }

    private bool WaitForNextGeneration(int lastGeneration)
    {
        // Fast path: a tick is published while the worker is still spinning.
        // The spin must happen outside _gate, otherwise the caller cannot
        // acquire the lock to publish the next tick.
        for (var spin = 0; spin < SpinIterations; spin++)
        {
            if (Volatile.Read(ref _generation) != lastGeneration)
            {
                return true;
            }

            if (_stop)
            {
                return false;
            }

            Thread.SpinWait(32);
        }

        // Slow path: park until the next tick. Monitor.Wait releases _gate and
        // reacquires it, so the caller can copy the published work safely.
        lock (_gate)
        {
            while (!_stop && _generation == lastGeneration)
            {
                Monitor.Wait(_gate);
            }

            return !_stop;
        }
    }

    private void ExecuteTick()
    {
        var phase1 = _phase1;
        var count1 = _count1;
        var phase2 = _phase2;
        var count2 = _count2;
        ExecuteTick(phase1, count1, phase2, count2, 0);
    }

    private void ExecuteTick(Action<int>? phase1, int count1, Action<int>? phase2, int count2, int worker)
    {
        var tickStart = Stopwatch.GetTimestamp();
        var busyStart = BusyStatsEnabled ? tickStart : 0;

        try
        {
            RunPhaseChunked(phase1, count1, ref _index1, _chunkSize);
        }
        catch (Exception exception)
        {
            RecordError(exception);
        }

        if (BusyStatsEnabled)
        {
            var now = Stopwatch.GetTimestamp();
            _phase1Busy[worker] += now - busyStart;
            busyStart = now;
        }

        // Networks must all be refreshed before nodes start: a node send must
        // not race with the refresh of the destination network in the same tick.
        _phaseBarrier.SignalAndWait();
        var networkPhaseEnd = Stopwatch.GetTimestamp();

        if (Volatile.Read(ref _error) == null)
        {
            try
            {
                RunPhaseChunked(phase2, count2, ref _index2, _chunkSize);
            }
            catch (Exception exception)
            {
                RecordError(exception);
            }
        }

        if (BusyStatsEnabled)
        {
            _phase2Busy[worker] += Stopwatch.GetTimestamp() - busyStart;
        }

        _phaseBarrier.SignalAndWait();

        LastNetworkPhaseTicks = networkPhaseEnd - tickStart;
        LastNodePhaseTicks = Stopwatch.GetTimestamp() - networkPhaseEnd;
        LastTickTicks = Stopwatch.GetTimestamp() - tickStart;
    }

    private void RecordError(Exception exception)
    {
        Interlocked.CompareExchange(ref _error, exception, null);
    }

    private static void RunPhase(Action<int>? body, int count, ref int index)
    {
        if (body == null || count <= 0)
        {
            return;
        }

        while (true)
        {
            var item = Interlocked.Increment(ref index) - 1;
            if (item >= count)
            {
                return;
            }

            body(item);
        }
    }

    /// <summary>
    /// Claims work in chunks of <paramref name="chunkSize"/> to reduce
    /// <see cref="Interlocked.Increment"/> contention on tiny per-item bodies.
    /// </summary>
    private static void RunPhaseChunked(Action<int>? body, int count, ref int index, int chunkSize)
    {
        if (body == null || count <= 0 || chunkSize <= 1)
        {
            RunPhase(body, count, ref index);
            return;
        }

        while (true)
        {
            var start = Interlocked.Add(ref index, chunkSize) - chunkSize;
            if (start >= count)
            {
                return;
            }

            var end = Math.Min(start + chunkSize, count);
            for (var i = start; i < end; i++)
            {
                body(i);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stop = true;
            Monitor.PulseAll(_gate);
        }

        foreach (var thread in _threads)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        _phaseBarrier.Dispose();
    }
}
