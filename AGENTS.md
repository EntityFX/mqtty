# AGENTS.md — MqttY (симулятор MQTT-брокеров и сетей)

Краткое руководство по репозиторию и **фактическому** состоянию продукта.

## 1. О проекте

**MqttY** — симулятор MQTT-брокеров и сетей в модельном времени: клиенты, брокеры, сетевые задержки, очереди и протокольные обмены без запуска реальных брокеров. Поддерживает профили брокеров MqttBenchmark и три движка: LoadBalancing, Relay, Federation.

Репозиторий: `https://github.com/entityfx/mqtty` (ветка `develop`).

**Стек:** C#, целевой фреймворк основных проектов — `net6.0`, сборка — .NET 8 SDK, Designer — .NET 8 + Avalonia.

## 2. Структура

```
mqtty/
├── AGENTS.md
├── README.md
├── config/                     # примеры конфигураций экспериментов
├── doc/
│   ├── benchmark-article.md    # статья seq vs par (числа могут отставать от кода)
│   └── optimization/
│       ├── optimization-results.md
│       ├── baseline-metrics.md
│       └── parallelization-plan.md   # актуальный план выхода на 4–8×
├── results/                    # результаты экспериментов (не коммитить)
├── src/EntityFX.MqttSimulator/
│   ├── EntityFX.MqttSimulator.Contracts/  # базовые контракты сети/счётчиков
│   ├── EntityFX.MqttY.Contracts.Mqtt/     # MQTT-контракты
│   ├── EntityFX.MqttY.Contracts.Scenarios/# контракты сценариев
│   ├── EntityFX.MqttSimulator/            # ядро: сеть, узлы, NetworkSimulator
│   ├── EntityFX.MqttY.Engines/            # LoadBalancing, Relay, Federation
│   ├── EntityFX.MqttY.LoadBalancing.App/  # CLI: demo, run, matrix
│   ├── EntityFX.MqttY.MqttRelay.App/      # CLI: relay, калибровка
│   ├── EntityFX.MqttY.Federation.App/     # CLI: федерация
│   ├── EntityFX.MqttY.Benchmark.Compare/  # reference-бенчмарк seq vs par
│   ├── EntityFX.MqttY.Plugin.Mqtt/        # MQTT-плагин, профили брокеров
│   ├── EntityFX.MqttY.Plugin.Scenarios/   # сценарии и действия
│   ├── EntityFX.MqttY.Web.App/            # веб-интерфейс и API
│   ├── EntityFX.MqttY.Cli/                # общий CLI
│   ├── EntityFX.MqttY.Designer/           # GUI-редактор (Avalonia)
│   ├── EntityFX.Tests.Integration/
│   └── EntityFX.Tests.Web/
├── tools/                      # скрипты измерений и анализа
└── utils/
```

## 3. Окружение

- .NET SDK: `6.0.136` и `8.0.121` (проверка: `dotnet --list-sdks`).
- `net6.0` выведен из поддержки — предупреждения `NETSDK1138` ожидаемы.
- Текущий хост: **8 логических CPU = 4 физических ядра / 8 потоков**. Это важно для оценки ускорения (см. §4).

## 4. Текущее состояние производительности

Параллельный режим (`parallel=true`) есть во всех трёх движках: параллельный двухфазный refresh (сети → узлы) на постоянном пуле `TickWorkerPool`. Флаг параллелизма **не** входит в `LoadBalancingOptions`/`FederationOptions` — он передаётся в метод раннера.

Фактические цифры reference-бенчмарка (прогретые медианы, текущий 4C/8T хост, 4 воркера):

| Движок | seq | par | wall speedup | refresh speedup | Комментарий |
|---|---:|---:|---:|---:|---|
| LoadBalancing | 3.34 с | 2.13 с | **1.57×** | **1.70×** | после per-session locks / ideal-sampler skip; ratio в пределах шума |
| Relay | 7.08 с | 3.02 с | **2.35×** | **2.47×** | лучший движок |
| Federation | 33.6 с | 17.2 с | **1.98×** | **2.49×** | lock-free ledger, SHA вне lock, Pump без OrderBy, IsIdle cache; Pump остаётся serial (parallel отклонён) |

**Чего пока нет:** целевых 4–8×. На 4C/8T потолок ~4–4.5× из-за SMT; 6–8× возможны только на 8 физических ядрах. План выхода — `doc/optimization/parallelization-plan.md`.

Кривая масштабирования (`DOTNET_PROCESSOR_COUNT` = 1/2/4/8): LB `1.16 → 1.93 → 1.99 → 1.61`, Relay `1.10 → 1.65 → 2.06 → 2.49`, Federation `1.12 → 1.57 → 1.81 → 1.67`. Для текущего кода на 4C/8T оптимум — **физические ядра (4)**; 8 логических хуже для LB из-за стоимости `Barrier` и SMT.

После M0/M1/M2/M2b по умолчанию: **физические ядра**, спин-барьер, serial publish и `nodeChunk=1`. `MQTTY_WORKERS`/`MQTTY_BARRIER`/`MQTTY_PARALLEL_PUBLISH` переопределяют. Главные оставшиеся тормоза — барьер (~27% wall при 4 воркерах) и инфляция работы ~1.4×; per-tick `Parallel.For` в `Drive` убран (он сжигал ~6× CPU и давал LB 1.0×).

Почему старый прогон нельзя было считать строгим гейтом (исправлено в M0):
- measurement = 0.5 с, фиксированные расходы сопоставимы с полезной работой;
- seq всегда запускался первым — второй прогон теплее (при `DOTNET_PROCESSOR_COUNT=1` видно 1.10–1.16× вместо 1.0×);
- экспорт результатов входил в wall-clock;
- Federation упиралась в `DrainTimeoutSeconds=1`.

Новый harness делает прогрев, чередует порядок, считает медиану, умеет `--no-export` и `--measure 3–5`; для гейта используйте именно его.

Масштабная проверка (4C/8T): LB L `2.27×/2.51×`, Relay L `2.12×/2.15×`, Fed L (non-overload) `1.93×/2.39×`; Fed на 10k rps перегружен (core mismatch) и для G4 не годится.

## 5. Известные ограничения и долги

- Federation: `Brokers ≤ 16`, публикаций ≤ 100 000, `publications × receivers ≤ 1 000 000`.
- `FederationDeliveryLedger` переведён на lock-free структуры (ConcurrentDictionary + per-receiver lock); `Pump` остаётся последовательным — параллельный вариант через `Parallel.For` оказался хуже (1.52× против 1.98×).
- `DeliveryLedger` LB переведён на lock-free структуры; `TotalSteps` кэшируется на тик; active-only refresh (`MQTTY_ACTIVE_ONLY=1`) реализован, но по умолчанию выключен — на плотной нагрузке выигрыша нет.
- Профиль parallel-only: **~50% времени — ожидание/спин** (`WaitForNextGeneration` 31%, `SpinBarrier` 19%); полезная node-работа ~29%; инфляция работы ×1.51 (10.12 с против 6.72 с sequential refresh); загрузка 3.33/4 ядра. Узкие места работы: `ToArray`/LINQ, pending-list snapshots, `ConcurrentDictionary`, `BrokerMeasurement`.
- Аллокации ~1.6–5 ГБ на прогон; per-tick `Parallel.For` в `Drive` убран (serial publish), `nodeChunk=1`.
- Проверены и не дали эффекта: shared LAN ingress, `MQTTY_NONATOMIC_ID`, packet pool, изоляция сетей, статическое распределение. Pointer-chasing 1→4 потока масштабируется на 96% — память не предел.
- Проверены и отклонены по замерам: pipeline network+outgoing (1.44× vs 1.55×), cost-aware LPT (1.38× vs 1.61×), parallel `Pump` (1.52× vs 1.98×), active-only на large LB (1.97× vs 2.19×), fast-idle (без эффекта), per-network dependency scheduler (small 1.51× vs 1.59×, large 1.94× vs 2.25×). Симуляция CPU-bound; очередь задач и спин дороже снятого барьера.
- 8 логических потоков на этом 4C/8T хосте хуже 4 физических: LB small 0.86× vs 1.65×, large 2.11× vs 2.29×. Для целевых 4–8× нужен хост с 8 физическими ядрами.
- Federation недетерминирован по receiver-латентности и `queueDrops` при перегрузке; core-метрики (counts, unique, goodput, publish p50–p99) совпадают.
- `StaticFederator.Pump` сортирует цели через `OrderBy` на каждый тик; `topology.Pump()` последовательный.
- `topology.IsIdle` в drain сканирует все сети/узлы/федераторы.
- `TotalSteps` перебирает `ThreadLocal.Values`; `NodeBase.Refresh` дергает его на каждом узле.
- Два теста падали **и на чистом `HEAD`**; сейчас исправлены, полный набор **388/388**. Регрессий нет.
- В рабочей копии могут быть незакоммиченные оптимизации (`NetworkSimulator.cs`, `Node.cs`, `TickWorkerPool.cs`, `StaticFederator.cs`, `FederationRunner.cs`, `FederationTopology.cs`, `InMemoryRepository.cs`). Не коммитьте их без согласия автора.

## 6. Сборка

```bash
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --nologo
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.LoadBalancing.App/EntityFX.MqttY.LoadBalancing.App.csproj -c Release --nologo
```

## 7. Запуск экспериментов и профилирование

### 7.1 Reference-бенчмарк (seq vs par)

```bash
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --nologo
dotnet run  --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build
```

Сейчас конфигурация задаётся аргументами (см. `--help`). Пример gate-прогона:

```bash
dotnet run --project .../EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build -- \
  --engines lb,relay,fed --repeat 5 --warmup-runs 1 --measure 3 --drain 10 --workers 4 --no-export --json results/gate.json
```

Ключевые опции: `--engines`, `--repeat`, `--warmup-runs`, `--workers`, `--measure`, `--warmup`, `--drain`, `--rps`, `--fed-rps`, `--brokers`, `--nets`, `--publishers`, `--relay-clients`, `--relay-repeats`, `--seed`, `--no-export`, `--no-strict`, `--json`.

Бенчмарк делает прогревочный прогон, чередует seq/par, считает медиану и сравнивает **core**-метрики строго, а receiver-латентность и `queueDrops` — как diagnostics.

### 7.2 Профиль фаз и воркеров

```bash
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 \
  dotnet run --project .../EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build
```

Печатает `refresh-stats` (wall/network/node/us_per_tick) и `worker-stats` (phase1/phase2/barrier, `p2_parallelism`).

### 7.3 Кривая масштабирования

```bash
for n in 1 2 4 8; do
  DOTNET_PROCESSOR_COUNT=$n dotnet run --project .../EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build
done
```

### 7.4 Переменные окружения

| Переменная | Назначение |
|---|---|
| `MQTTY_REFRESH_STATS=1` | статистика refresh по фазам |
| `MQTTY_WORKER_STATS=1` | занятость воркеров и барьера |
| `MQTTY_NODE_CHUNK=N` | размер чанка узлов (по умолчанию 1) |
| `MQTTY_NETWORK_CHUNK=N` | размер чанка сетей |
| `MQTTY_NODE_STATS=1` | per-node / per-type профиль занятости |
| `MQTTY_WORKERS=N` | число воркеров refresh (по умолчанию — физические ядра) |
| `MQTTY_BARRIER=spin\|monitor` | выбор барьера (по умолчанию spin при `workers ≤ physical`, иначе monitor) |
| `MQTTY_SPIN_ITERATIONS=N` | сколько крутиться перед парковкой воркера |
| `MQTTY_PARALLEL_PUBLISH=1` | параллельная публикация (по умолчанию serial; только для больших топологий) |
| `MQTTY_ACTIVE_ONLY=1` | пропускать quiescent-узлы/сети (по умолчанию off) |
| `MQTTY_DISABLE_COUNTERS=1` | отключить счётчики (ускоряет прогон) |
| `MQTTY_ISOLATE_NETWORKS=1` | изолировать сети клиентов в LB |
| `MQTTY_NONATOMIC_ID=1` | неатомарная выдача packet id (только для замеров) |
| `MQTTY_NO_PACKET_POOL=1` | отключить пул пакетов |
| `DOTNET_PROCESSOR_COUNT=N` | ограничить число логических CPU |

### 7.5 Другие запуски

```bash
# LB demo
dotnet run --project .../EntityFX.MqttY.LoadBalancing.App.csproj -c Release --no-build -- \
  demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42 --output results/demo-run

# Интеграционные тесты
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo
```

## 8. Ключевые компоненты

| Компонент | Путь | Назначение |
|---|---|---|
| `NetworkSimulator` | `EntityFX.MqttSimulator/Network/NetworkSimulator.cs` | Тик, `Refresh(parallel, strategy)`, порядок фаз |
| `TickWorkerPool` | `EntityFX.MqttSimulator/Network/TickWorkerPool.cs` | Постоянный пул, двухфазный refresh, `Barrier` |
| `Node` / `Network` | `EntityFX.MqttSimulator/Network/` | Очереди, отложенные пакеты, quiescence |
| `LoadBalancingOptions` / `...Runner` | `Engines/LoadBalancing/` | Параметры и запуск LB |
| `FederationOptions` / `...Runner` | `Engines/Federation/` | Параметры, запуск, drain |
| `FederationDeliveryLedger` | `Engines/Federation/Delivery/` | Учёт публикаций/доставок (сейчас глобальный lock) |
| `StaticFederator` | `Engines/Federation/Core/` | Очереди федератора, `Pump`, `IsIdle` |
| `MqttRelayApp` | `Engines/Relay/MqttRelayApp.cs` | Запуск relay-симуляции |
| `Benchmark.Compare` | `EntityFX.MqttY.Benchmark.Compare/Program.cs` | Reference-бенчмарк seq/par |

## 9. Правила коммитов

- Коммитьте только свои файлы; не трогайте чужие незакоммиченные правки.
- `results/` не коммитится; `bin/` и `obj/` исключены.
- Числа из бенчмарка приводите с указанием CPU/числа физических ядер, числа повторов и медианы.

## 10. Документация

- `README.md` — быстрый старт.
- `doc/optimization/parallelization-plan.md` — **актуальный план** оптимизации и критерии приёмки.
- `doc/optimization/scaling-forecast.md` — математическая модель и прогноз масштабирования (per-tick decomposition, прогноз для 8/16 ядер).
- `doc/code-analysis.md` — верификация code-level гипотез (что подтвердилось, что уже исправлено, что отклонено замерами).
- `doc/benchmark-article.md` — статья; её цифры могут отставать от текущего кода.
- Внешняя документация: `github.com/EntityFX/mqtty-meta`.
