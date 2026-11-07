# Code-Level Parallelization Bottlenecks — verification report

Проверка исходного анализа по фактическому коду и замерам (2026-11-08,
i7-3770: 4 физических / 8 логических ядер). Все цифры получены
`EntityFX.MqttY.Benchmark.Compare`, счётчики отключены, экспорт отключён.

## Сводка гипотез

| # | Гипотеза | Статус | Факт |
|---|---|---|---|
| 1 | SpinBarrier starves on SMT | **уже исправлено** | `ResolveBarrier()` выбирает `spin` при `workers ≤ physical`, иначе `monitor`; на 8 logical: auto 3.88 с, принудительный spin 6.01 с |
| 2 | `Counters.Refresh()` каждый тик даёт α=1.51 | **не подтверждено** | `NodeBase.Refresh` вызывает counters только при `EnableCounters`; `CurrentRefreshSteps` кэшируется на тик. ON vs OFF: wall 2.720 vs 2.700 с (<1%), CPU одинаковый 9.39 с |
| 3 | Federation `Pump()` последовательный → потенциал 1.98× | **частично, fix неверный** | Pump действительно serial, но `Parallel.For`/`ForEach` на каждый тик **хуже**: 1.52× против 1.98× (ready-queue/спин дороже работы) |
| 4 | `IsIdle` сканирует всех федераторов каждый drain tick | **уже исправлено** | `StaticFederator.IsIdle` — O(1) `_activeCount`; `FederationTopology.IsIdle` кэшируется на тик. Остаётся один `Federators.All` на тик (16 элементов) — незначимо |
| 5 | LINQ/`ToArray` каждый тик — часть α | **подтверждено частично** | trace: `EnumerableHelpers.ToArray` 5.4% exclusive, `StorageExtensions.GetPendingMessages` 3.4%. Глобальные static lock'и уже убраны (per-session), но `GetPendingMessages().FirstOrDefault(...)` всё ещё копирует список |
| 6 | `Interlocked` packet id — конкуренция | **не подтверждено** | `MQTTY_NONATOMIC_ID=1`: 2.80 с vs 2.59 с atomic; выигрыша нет (скорее шум/порча id) |

## Подтверждённые факты профиля

- **α = 1.51** на 4 физических ядрах: parallel work 10.12 с против sequential refresh 6.72 с (LB small, 10 с модельного времени).
- **b ≈ 10 мкс/тик/воркер**: barrier 0.98 с / 100151 тиков (small), 0.31 с / 30151 (large) → 9.8–10.3 мкс.
- **h ≈ 2 мкс/тик**: handoff/прочие накладные.
- **~50% сэмплов — ожидание/спин**: `WaitForNextGeneration` 31%, `SpinBarrier` 19%.
- **Загрузка 3.33 из 4 ядер**; 8 logical хуже 4 (SMT).
- Основная полезная работа: `Node.Refresh` inclusive 29%; hot spots — `ToArray`/LINQ, pending-list snapshots, `ConcurrentDictionary`, `BrokerMeasurement`.

## Что из исходного анализа неверно

1. **«Counter refresh — главная причина α=1.51»** — нет. Counters выключены в гейте, ON/OFF разница <1%; при включённых counters `TotalSteps` уже кэшируется на тик.
2. **«Packet ID contention»** — нет. Non-atomic id не ускоряет.
3. **«Parallel Pump даст 1.98×»** — наоборот: 1.52× (измерено), потому что per-tick `Parallel.For` дороже работы.
4. **«Autodetect барьера надо добавить»** — уже есть (`TickWorkerPool.ResolveBarrier` + `CpuTopology`).
5. **«IsIdle scans all federators»** — уже O(1) на федератора + кэш на тик; топология сканирует 16 федераторов один раз за тик.
6. **«4.0–6.5× на 4C/8T после skills»** — математически невозможно: идеальный предел small на 8 physical — 3.94×, large — 4.64×; на 4C/8T текущий потолок ~2.5–3×.

## Что реально осталось

| Приоритет | Работа | Ожидание |
|---|---|---|
| P0 | Снизить α: убрать оставшиеся копирования pending/subscriptions (`FirstOrDefault` без `ToArray`), LINQ в hot path, allocations/GC | α 1.51 → 1.0–1.2; это единственный путь к 4× на 8 ядрах |
| P1 | Снизить b: барьер/хвост; scheduling-эксперименты (pipeline, per-network, cost-aware, parallel Pump) уже отклонены | b 10 → <5 мкс/тик |
| P2 | Federation: serial Pump оставить; оптимизировать node-работу/ledger дальше | Fed ~2.0–2.5× → 2.5–3× |
| P3 | 8+ физических ядер | на 16 physical идеальный предел large 6.28× |

**Вывод:** исходный документ правильно указал на α и b как на главные факторы, но ошибся в причинах α и в оценках потенциала. Математическая модель и прогноз — в `doc/optimization/scaling-forecast.md`.

## Команды проверки

```bash
# counters ON/OFF
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 \
  dotnet run --project .../EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build -- \
  --engines lb --mode par --repeat 1 --warmup-runs 0 --measure 5 --no-export --workers 4
# добавить MQTTY_DISABLE_COUNTERS=1 для OFF

# barrier auto vs spin на 8 logical
MQTTY_BARRIER=spin ... --workers 8

# packet id
MQTTY_NONATOMIC_ID=1 ... --workers 4
```
