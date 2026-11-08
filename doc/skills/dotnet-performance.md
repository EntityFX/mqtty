# Skill: C#/.NET performance and parallelization (MqttY)

Практический чек-лист для оставшихся работ. Источник: локальные skills
`.opencode/skills/csharp-parallel` и `.opencode/skills/mqtty-benchmark`,
сведённые в один документ. Ссылки: [performance.md](../performance.md),
[progress.md](../progress.md).

## Rule 0 — сначала измерение

Никогда не оптимизировать по интуиции. До изменения зафиксировать:

- single-thread baseline (seq wall / refresh);
- parallel wall / refresh и медиану ≥ 5 прогонов;
- фазы (`MQTTY_PHASE_STATS=1`), phase1/phase2/barrier (`MQTTY_WORKER_STATS=1`);
- число физических ядер, SMT и CV.

Оптимизация без before/after — не сделана.

## Workflow

1. **Найти serial fraction.** Amdahl: `S(n) = 1 / ((1-p) + p/n)`. Если
   фиксированные расходы — 30 % baseline, 4× end-to-end недостижимы, пока они
   не убраны.
2. **Классифицировать hot path:** parallel work, serial work, synchronization,
   contention (locks/atomics/false sharing), allocation/GC.
3. **Сначала убрать сериализацию, потом добавлять потоки.** Более дешёвый
   барьер или меньше задач лучше, чем больше воркеров.
4. **Писать только в своё.** Один writer на сущность за тик. Общее состояние —
   read-only snapshot или явная синхронизация.
5. **Snapshot вместо lock** для read-mostly структур. Не держать глобальный
   lock во время CPU-работы (hashing, formatting, sorting).
6. **Мерить хвост.** `sum(worker busy) / max(worker busy)` — эффективный
   параллелизм; `max / avg` — дисбаланс.
7. **После каждого шага** — тесты, `core=match`, повторный замер.

## Протокол бенчмарка

```bash
# LB L
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj \
  -c Release --no-build -- --engines lb --repeat 7 --warmup-runs 1 --measure 3 --drain 10 \
  --workers 12 --brokers 64 --nets 4 --publishers 256 --rps 20000 --no-export --no-strict

# Relay / Federation
dotnet run ... -- --engines relay --repeat 5 --warmup-runs 1 --measure 3 --drain 10 \
  --workers 12 --fed-rps 2000 --no-export --no-strict
dotnet run ... -- --engines fed --repeat 7 --warmup-runs 1 --measure 1 --drain 10 \
  --workers 12 --brokers 16 --publishers 256 --fed-rps 2000 --no-export --no-strict
```

Правила: Release + `--no-build`, один warm-up, чередование seq/par, медиана
≥ 5, `--no-export` для гейта, drain ≥ 10 с для Federation.

## Что читать в диагностике

- `refresh-stats`: wall, network, node, `us_per_tick`.
- `worker-stats`: phase1/phase2/barrier busy, `p2_parallelism`.
- barrier share ≈ `barrier sum / (wall × workers)`.
- effective parallelism ≈ `phase2 sum / phase2 max`.
- `p2_parallelism` ~7–8 → работа параллелится, потери в sync/serial коде.
- `MQTTY_NODE_STATS=1`: время и аллокации по узлам/типам.
- `MQTTY_PHASE_STATS=1`: wall/refresh по фазам LB, Relay, Federation.

## Анти-паттерны этого репозитория

- `System.Threading.Barrier` дважды на микротик: monitor-signalling дороже
  sense-reversing `Interlocked` + `SpinWait`.
- Один глобальный `lock` вокруг publication lookup/delivery/hash.
- `LINQ OrderBy` на сущность за тик; порядок предвычислять один раз.
- `Parallel.For` на очень короткий тик: scheduling дороже работы.
- `ThreadLocal<T>.Values` на каждый узел за тик; агрегат кэшировать на тик.
- Пересборка immutable-словарей в hot getter; кэш по версии топологии.
- Хеширование одного и того же payload на каждую пересылку.
- Проверка «idle» дороже самой работы; сначала измерить стоимость проверки.

## Barrier checklist

- arrival counter + sense generation;
- last arriver сбрасывает counter, пишет generation, возвращается;
- остальные крутят `SpinWait.SpinOnce` на `Volatile.Read`;
- `Interlocked` даёт full fence; acquire/release не терять;
- нет аллокаций и kernel transition в common case;
- `MQTTY_WORKER_STATS=1`: barrier share < 10 % refresh wall.

## Validation gate

- Тот же seed/топология; seq и par дают одинаковые scientific metrics
  (`core=match`).
- Нет новых падений тестов; известные baseline-падения — явно.
- Медиана ≥ 5 прогонов, не один запуск.
- Рядом с каждым числом — физические ядра, SMT, workers.
- Регрессия wall/refresh > 5 % или `core=MISMATCH` — откат.

## Текущие ориентиры (12C/24T, workers=12)

| Конфиг | wall | refresh |
|---|---:|---:|
| LB L | ~5.0× | ~6.0× |
| LB reference | 2.98× | 3.36× |
| Relay | ~5.3× | ~6.3× |
| Federation L | 3.9–4.2× | 4.9–5.3× |

Гейты и остаток: [progress.md](../progress.md). Отклонённые эксперименты:
[performance.md](../performance.md).
