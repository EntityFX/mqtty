---
name: MqttY Benchmark
description: Run, extend, and interpret MqttY seq-vs-par benchmarks and phase profiles. Use for performance gates, scaling curves, and before/after optimization measurements.
---

# MqttY Benchmark

## Source of truth

- Reference harness: `src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/Program.cs`.
- Gates and current baseline: `doc/performance.md`.
- Do not create a second benchmark project or extra docs.

## Hardware first

Record `lscpu`: logical CPUs, threads per core, physical cores, NUMA.
This host is 24 logical / 12 physical (Xeon E5-2680 v3). Use `--workers 12`
(physical cores); SMT oversubscription at 24 workers regresses LB. Gate is
≥ 4× refresh/wall on 12 physical cores; 3.5–4.5× is the 4C/8T fallback.

## Fair protocol

1. Build Release; use `--no-build` for runs.
2. One discarded warm-up pair (JIT, caches).
3. Alternate seq/par order between repetitions (`seq→par`, then `par→seq`).
4. Median of at least 5 runs; report min/max and CV.
5. Same seed, topology, workload for both modes.
6. Exclude result export from the end-to-end gate (or report it separately).
7. Use a measurement window large enough that refresh dominates fixed costs
   (≥ 3 s for the gate; 0.5 s is only a smoke test).
8. For Federation set drain timeout ≥ 10 s, otherwise `status=failed` truncates work.

At `DOTNET_PROCESSOR_COUNT=1` the harness must show 1.00×; a higher value means
measurement bias (usually warm-up order). Fix the protocol before trusting results.

## Profiling

```bash
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 \
  dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj \
  -c Release --no-build
```

Read:
- `refresh-stats`: wall, network, node, `us_per_tick`.
- `worker-stats`: phase1/phase2/barrier busy, `p2_parallelism`.
- Barrier share ≈ `barrier sum / (wall × workers)`.
- Effective parallelism ≈ `phase2 sum / phase2 max`.
- If `p2_parallelism` is already ~7–8, the work parallelizes; the loss is sync or serial code.

Scaling curve:

```bash
for n in 1 2 4 8; do
  DOTNET_PROCESSOR_COUNT=$n dotnet run --project ... --no-build
done
```

## Correctness gate

Compare seq vs par: scheduled, attempts, rejected, unique delivered, goodput,
p50/p95/p99, errors. Counts must match exactly; errors must be zero.
A faster run with different metrics is a regression, not an optimization.

## Reporting

Record: CPU model, physical/logical cores, workers, median, min/max, CV, phase breakdown,
and the exact command. Put results in `doc/performance.md`;
do not add new report files.
