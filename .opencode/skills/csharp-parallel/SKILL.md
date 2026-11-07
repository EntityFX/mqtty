---
name: C# Parallelization
description: Parallelize C#/.NET hot paths safely and measurably (worker pools, tick barriers, work distribution, determinism). Use when changing MqttY refresh, runners, or any per-tick simulation code.
---

# C# Parallelization

## Rule 0 — measure before and after

Never optimize by intuition. Record the single-thread baseline, the parallel wall time,
per-phase timing, and CPU utilization first. Use the `mqtty-benchmark` skill for this repo.
Optimization without a before/after number is not done.

## Workflow

1. **Find the serial fraction.** Amdahl: `S(n) = 1 / ((1-p) + p/n)`.
   On 8 logical CPUs with 4 physical cores the realistic ceiling is ~4–4.5×.
   If fixed overhead is 30 % of the baseline, 4× end-to-end is impossible until it is removed.
2. **Classify each hot path** as one of: parallel work, serial work, synchronization,
   contention (locks/atomics/false sharing), allocation/GC.
3. **Remove serialization before adding threads.** A cheaper barrier or fewer tasks
   beats more workers.
4. **Keep writes disjoint.** One writer per entity per tick. Shared state is read-only
   snapshot or explicitly synchronized.
5. **Prefer snapshots over locks** for read-mostly structures. Never hold a global
   lock while doing CPU work (hashing, formatting, sorting).
6. **Measure the tail.** `sum(worker busy) / max(worker busy)` shows effective parallelism;
   `max / avg` shows imbalance.
7. **Re-run correctness and determinism checks** after every step.

## Anti-patterns seen in this repo

- `System.Threading.Barrier` twice per microsecond-scale tick: monitor-based signalling
  costs ~40 µs/worker and grows with participant count. Prefer a sense-reversing
  `Interlocked` barrier with `SpinWait` fallback.
- A "pump until idle" inner loop that cannot progress because the graph is not advanced.
  Drain must always be: `pump once → advance graph once → check idle`.
- One global `lock` around publication lookup/delivery/hash: serializes the whole engine.
- `LINQ OrderBy` per entity per tick; precompute the order once.
- `Parallel.For` per very short tick: thread-pool scheduling can cost more than the work.
- `ThreadLocal<T>.Values` scanned per node per tick; cache the aggregate once per tick.
- Rebuilding immutable dictionaries in hot getters; cache by topology version.

## Barrier implementation checklist

- Arrival counter + generation (sense) counter.
- Last arriver resets the counter, flips generation with `Volatile.Write`, returns.
- Others spin with `SpinWait.SpinOnce` on generation with `Volatile.Read`.
- `Interlocked` operations provide the full fence; do not lose acquire/release.
- No allocation and no kernel transition in the common case.
- Verify with `MQTTY_WORKER_STATS=1`: barrier share should fall below 10 % of refresh wall.

## Validation gate

- Same seed and topology; seq and par must produce identical scientific metrics.
- No new test failures; known baseline failures stay explicit.
- Report median of ≥ 5 runs, not a single run.
- State physical core count and SMT status next to every speedup number.
