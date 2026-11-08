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
///
/// The phase fence is a sense-reversing spin barrier. The built-in
/// <see cref="System.Threading.Barrier"/> uses monitor signalling on every pass;
/// a refresh tick is only tens of microseconds of work, so that signalling cost
/// dominated the tick and grew with the number of workers.
/// </summary>
internal sealed class TickWorkerPool : IDisposable
{
    private readonly RuntimeOptions _runtime;
    private readonly int _workerCount;
    private readonly Thread[] _threads;
    private readonly int[] _workerGenerations;
    private readonly long[] _phase1Busy;
    private readonly long[] _phase2Busy;
    private readonly long[] _barrierBusy;
    private readonly IPhaseBarrier _phaseBarrier;
    private readonly object _gate = new();

    private volatile bool _stop;
    private int _generation;

    private Action<int>? _phase1;
    private Action<int>? _phase2;
    private int _count1;
    private int _count2;
    private int _index1;
    private int _index2;
    private int _chunkSize1;
    private int _chunkSize2;
    private bool _twoPhase = true;
    private Exception? _error;

    public TickWorkerPool(int workerCount, RuntimeOptions? runtime = null)
    {
        _runtime = runtime ?? RuntimeOptions.FromEnvironment();
        _workerCount = Math.Max(1, workerCount);
        _phaseBarrier = ResolveBarrier(_workerCount);
        _phase1Busy = new long[_workerCount];
        _phase2Busy = new long[_workerCount];
        _barrierBusy = new long[_workerCount];

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
    public long[] BarrierBusy => _barrierBusy;

    /// <summary>
    /// Runs both refresh phases. <paramref name="phase1"/> and
    /// <paramref name="phase2"/> are executed by all workers; the caller also
    /// participates. Any exception from a phase is rethrown on the caller after
    /// all workers reached the final barrier.
    /// </summary>
    public void Run(Action<int>? phase1, int count1, Action<int>? phase2, int count2)
    {
        Run(phase1, count1, phase2, count2, chunkSize1: 1, chunkSize2: 1);
    }

    /// <summary>
    /// Runs a single parallel phase and fences before returning. Used for work
    /// that must finish between two simulator ticks (for example federation
    /// pumping) without paying the second phase fence.
    /// </summary>
    public void RunSingle(Action<int>? body, int count, int chunkSize)
    {
        if (count <= 0 || body == null)
        {
            return;
        }

        if (_threads.Length == 0)
        {
            var start = Stopwatch.GetTimestamp();
            _index1 = 0;
            RunPhaseChunked(body, count, ref _index1, chunkSize);
            var end = Stopwatch.GetTimestamp();
            LastNetworkPhaseTicks = end - start;
            LastNodePhaseTicks = 0;
            LastTickTicks = end - start;
            return;
        }

        lock (_gate)
        {
            _phase1 = body;
            _phase2 = null;
            _count1 = count;
            _count2 = 0;
            _index1 = 0;
            _index2 = 0;
            _error = null;
            _chunkSize1 = chunkSize;
            _chunkSize2 = 1;
            _twoPhase = false;
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

    /// <summary>
    /// Runs both refresh phases with chunked work claiming. Each worker grabs
    /// <paramref name="chunkSize1"/> networks or <paramref name="chunkSize2"/>
    /// nodes per Interlocked operation. Node work is very uneven (an idle
    /// publisher vs a broker with many subscribers), so nodes are claimed one
    /// at a time; otherwise a heavy chunk claimed last becomes the tick tail.
    /// </summary>
    public void Run(Action<int>? phase1, int count1, Action<int>? phase2, int count2,
        int chunkSize1, int chunkSize2)
    {
        if (_threads.Length == 0)
        {
            var start = Stopwatch.GetTimestamp();
            _index1 = 0;
            _index2 = 0;
            RunPhaseChunked(phase1, count1, ref _index1, chunkSize1);
            var networkEnd = Stopwatch.GetTimestamp();
            RunPhaseChunked(phase2, count2, ref _index2, chunkSize2);
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
            _chunkSize1 = chunkSize1;
            _chunkSize2 = chunkSize2;
            _twoPhase = true;
            _generation++;
            Monitor.PulseAll(_gate);
        }

        ExecuteTick();

        var error = Volatile.Read(ref _error);
        if (error != null)
        {
            throw new AggregateException(error);
        }

        // The final phase barrier above already guarantees that every worker
        // left the tick, so there is nothing else to wait for here.
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
            bool twoPhase;

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
                twoPhase = _twoPhase;
            }

            ExecuteTick(phase1, count1, phase2, count2, worker + 1, twoPhase);
        }
    }

    private bool WaitForNextGeneration(int lastGeneration)
    {
        // Fast path: a tick is published while the worker is still spinning.
        // The spin must happen outside _gate, otherwise the caller cannot
        // acquire the lock to publish the next tick.
        for (var spin = 0; spin < _runtime.SpinIterations; spin++)
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
        var twoPhase = _twoPhase;
        ExecuteTick(phase1, count1, phase2, count2, 0, twoPhase);
    }

    private void ExecuteTick(Action<int>? phase1, int count1, Action<int>? phase2, int count2, int worker,
        bool twoPhase)
    {
        var tickStart = Stopwatch.GetTimestamp();
        var busyStart = _runtime.WorkerStats ? tickStart : 0;

        try
        {
            RunPhaseChunked(phase1, count1, ref _index1, _chunkSize1);
        }
        catch (Exception exception)
        {
            RecordError(exception);
        }

        if (_runtime.WorkerStats)
        {
            var now = Stopwatch.GetTimestamp();
            _phase1Busy[worker] += now - busyStart;
            busyStart = now;
        }

        // Networks must all be refreshed before nodes start: a node send must
        // not race with the refresh of the destination network in the same tick.
        var barrierStart = Stopwatch.GetTimestamp();
        _phaseBarrier.SignalAndWait();
        var networkPhaseEnd = Stopwatch.GetTimestamp();
        if (_runtime.WorkerStats)
        {
            _barrierBusy[worker] += networkPhaseEnd - barrierStart;
            // Phase 2 timing must start after the barrier, otherwise the wait
            // for the slowest network worker is charged to the node phase.
            busyStart = networkPhaseEnd;
        }

        if (Volatile.Read(ref _error) == null)
        {
            try
            {
                RunPhaseChunked(phase2, count2, ref _index2, _chunkSize2);
            }
            catch (Exception exception)
            {
                RecordError(exception);
            }
        }

        if (_runtime.WorkerStats)
        {
            _phase2Busy[worker] += Stopwatch.GetTimestamp() - busyStart;
        }

        barrierStart = Stopwatch.GetTimestamp();
        _phaseBarrier.SignalAndWait();
        if (_runtime.WorkerStats)
        {
            _barrierBusy[worker] += Stopwatch.GetTimestamp() - barrierStart;
        }
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

    private IPhaseBarrier ResolveBarrier(int workers)
    {
        if (_runtime.Barrier == TickBarrierMode.Spin)
        {
            return new SpinBarrier(workers, _runtime.BarrierSpinIterations);
        }

        if (_runtime.Barrier == TickBarrierMode.Monitor)
        {
            return new MonitorBarrier(workers);
        }

        // One worker per physical core: a spin fence is fastest. More workers
        // than physical cores means SMT oversubscription, where spinners starve
        // the worker they wait for, so use the blocking BCL barrier.
        return workers <= CpuTopology.PhysicalCoreCount()
            ? new SpinBarrier(workers, _runtime.BarrierSpinIterations)
            : new MonitorBarrier(workers);
    }

    private interface IPhaseBarrier : IDisposable
    {
        void SignalAndWait();
    }

    /// <summary>Default fence: the BCL Barrier (monitor-based, efficient when oversubscribed).</summary>
    private sealed class MonitorBarrier : IPhaseBarrier
    {
        private readonly Barrier _barrier;
        public MonitorBarrier(int participants) => _barrier = new Barrier(participants);
        public void SignalAndWait() => _barrier.SignalAndWait();
        public void Dispose() => _barrier.Dispose();
    }

    /// <summary>
    /// Sense-reversing spin fence for one worker per physical core. The last
    /// arrival flips the generation; the others spin on it. It is faster than
    /// the monitor barrier when the worker count equals the physical core count,
    /// but it must not be used on an SMT-oversubscribed machine (spinners starve
    /// the worker they are waiting for).
    /// </summary>
    private sealed class SpinBarrier : IPhaseBarrier
    {
        private readonly int _participants;
        private readonly int _spinLimit;
        private int _remaining;
        private int _generation;

        public SpinBarrier(int participants, int spinLimit)
        {
            _participants = Math.Max(1, participants);
            _spinLimit = Math.Max(0, spinLimit);
            _remaining = _participants;
        }

        public void SignalAndWait()
        {
            var generation = Volatile.Read(ref _generation);
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                Volatile.Write(ref _remaining, _participants);
                Volatile.Write(ref _generation, generation + 1);
                return;
            }

            var spin = 0;
            while (Volatile.Read(ref _generation) == generation)
            {
                if (spin++ < _spinLimit)
                {
                    Thread.SpinWait(32);
                }
                else
                {
                    Thread.Yield();
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
