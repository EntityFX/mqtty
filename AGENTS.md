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
│   ├── architecture.md         # устройство, инварианты, статус S0–S5
│   └── performance.md          # методика, результаты, отклонённые оптимизации
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
- Текущий хост: **24 логических CPU = 12 физических ядер / 24 потока** (Xeon E5-2680 v3). Для гейта используйте `workers=12` (физические ядра); SMT-переподписка на 24 потоках даёт регрессию.

## 4. Текущее состояние производительности

Параллельный режим (`parallel=true`) есть во всех трёх движках: двухфазный
refresh (сети → fence → узлы → fence) на постоянном пуле `TickWorkerPool`.
Параллельный refresh всегда использует flat two-phase (strategy 0);
`strategy=1` — только sequential. Флаг параллелизма передаётся в метод раннера,
а не в `LoadBalancingOptions`/`FederationOptions`.

Текущий reference-бенчмарк (12C/24T, workers=12, медианы, `--no-export`):

| Конфиг | wall speedup | refresh speedup | core |
|---|---:|---:|---|
| LB L (64 брокера, 256 publishers, 20k rps) | **5.06×** | **6.03×** | match |
| LB reference | **2.98×** | **3.36×** | match |
| Relay (2k rps) | **5.44×** | **6.30×** | match |
| Federation L (16 брокеров, 256 publishers, 2k rps) | **3.97–4.14×** | **4.93–5.22×** | match / diag differ |

Ключевые факты:

- по умолчанию — физические ядра, spin-барьер при `workers ≤ physical`,
  serial publish и `nodeChunk=1`;
- `MQTTY_WORKERS`/`MQTTY_BARRIER`/`MQTTY_PARALLEL_PUBLISH` — только для
  экспериментов; `--workers 24` на 12 физических ядрах даёт SMT-переподписку и
  катастрофичен для LB;
- бенчмарк и единый CLI собираются с `ServerGarbageCollection=true` и
  `ConcurrentGarbageCollection=false`; между сэмплами делается полный GC вне
  измеряемого участка;
- главные оставшиеся тормоза — инфляция работы узлов (~1.8× в параллели) и
  Federation drain (~45 % его wall). Барьер после сортировки сетей — это в
  основном дисбаланс тяжёлых узлов, а не стоимость fence.

Подробная методика, фазовый профиль и отклонённые эксперименты —
`doc/performance.md`.

## 5. Известные ограничения и долги

- Federation: `Brokers ≤ 16`, публикаций ≤ 100 000,
  `publications × receivers ≤ 1 000 000`.
- Federation недетерминирован по receiver-латентности и `queueDrops` при
  перегрузке; core-метрики (counts, unique, goodput, publish p50–p99)
  совпадают. `diagnostics=differ` у Fed L воспроизводится и на baseline.
- `FederationDeliveryLedger` — `ConcurrentDictionary` + per-receiver lock;
  `StaticFederator.Pump` идёт параллельно на пуле тика (`RunSingle`), дробление
  до пар «федератор × target» выигрыша не дало.
- `DeliveryLedger` LB lock-free; `TotalSteps` кэшируется на тик через
  `CurrentRefreshSteps`; active-only refresh реализован, но по умолчанию
  выключен — на плотной нагрузке выигрыша нет.
- Главные остаточные резервы: инфляция node-работы ~1.8× в параллели и
  Federation drain ≈ 45 % его wall. Подробности — `doc/performance.md`.
- Тесты: Integration **436/436**, Web **62/62** (запускать последовательно,
  не параллельно).
- Не коммитьте чужие незакоммиченные правки; `results/`, `bin/`, `obj/` не
  коммитятся.

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

Бенчмарк делает прогревочный прогон, чередует seq/par, считает медиану и сравнивает **core**-метрики строго, а receiver-латентность и `queueDrops` — как diagnostics. Каждый сэмпл пишется в уникальный каталог экспорта; `MQTTY_WORKERS` восстанавливается после сэмпла. `activity=ok` требует `steps>0` и для LB/Fed `attempts>0 && unique>0`; иначе статус `invalid`, а strict возвращает 2. Ошибка одного сэмпла не роняет отчёт целиком.

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
| `MQTTY_QOS2_COMPLETED_TTL=N` | сколько тиков хранить завершённые QoS2-маркеры (по умолчанию 4096) |
| `MQTTY_QOS2_COMPLETED_CAP=N` | максимум завершённых QoS2-маркеров на сессию (по умолчанию 4096) |
| `DOTNET_PROCESSOR_COUNT=N` | ограничить число логических CPU |

### 7.5 Другие запуски

```bash
# LB demo
dotnet run --project .../EntityFX.MqttY.LoadBalancing.App.csproj -c Release --no-build -- \
  demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42 --output results/demo-run

# Интеграционные тесты
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo

# Web tests (в этом окружении restore требует отключения проверки подписи NuGet)
DOTNET_NUGET_SIGNATURE_VERIFICATION=false \
  dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Web/EntityFX.Tests.Web.csproj -c Release --nologo
```

Integration и Web наборы нельзя запускать одновременно: API-тесты поднимают
in-process host и делят временные каталоги, из-за чего часть тестов падает по
таймаутам. Запускайте последовательно; Web — 62/62, Integration — 436/436.

## 8. Ключевые компоненты

| Компонент | Путь | Назначение |
|---|---|---|
| `NetworkSimulator` | `EntityFX.MqttSimulator/Network/NetworkSimulator.cs` | Тик, `Refresh(parallel, strategy)`, порядок фаз |
| `SimulationClock` / `TickPipeline` | `EntityFX.MqttSimulator/Network/SimulationClock.cs`, `TickPipeline.cs` | Модельные часы, `TickContext`, стадии `PublishClock → RefreshTopology → CompleteTick` |
| `TopologyRegistry` / `TopologySnapshot` | `EntityFX.MqttSimulator/Network/TopologyRegistry.cs` | Реестры топологии и неизменяемый снапшот на тик |
| `TokenWindowAdmission` | `EntityFX.MqttSimulator/Network/TokenWindowAdmission.cs` | Rolling byte budget: reserve/commit/rollback |
| `PacketTrace` | `EntityFX.MqttSimulator/Network/PacketTrace.cs` | S0 characterization: NDJSON send/receive-трасса (env-gated) |
| `NodePacketSlot` / `NetworkPacketSlot` | `EntityFX.MqttSimulator/Network/NodePacketSlot.cs`, `NetworkPacketSlot.cs` | Data-oriented транспортные слоты (S2): без wrapper/pool, path-массив + NextHop |
| `CreditLoadDriver` / `HistogramStatistics` | `EntityFX.MqttY.Engines/Execution/` | Общий credit-драйвер нагрузки, drain-loop и гистограммы LB/Fed |
| `TickWorkerPool` | `EntityFX.MqttSimulator/Network/TickWorkerPool.cs` | Постоянный пул, двухфазный refresh, spin/monitor fence |
| `Node` / `Network` | `EntityFX.MqttSimulator/Network/` | Очереди, отложенные пакеты, quiescence |
| `LoadBalancingOptions` / `...Runner` | `Engines/LoadBalancing/` | Параметры и запуск LB |
| `FederationOptions` / `...Runner` | `Engines/Federation/` | Параметры, запуск, drain |
| `FederationDeliveryLedger` | `Engines/Federation/Delivery/` | Учёт публикаций/доставок, `ConcurrentDictionary` + per-receiver lock |
| `StaticFederator` | `Engines/Federation/Core/` | Очереди федератора, `Pump`, `IsIdle` |
| `MqttRelayApp` | `Engines/Relay/MqttRelayApp.cs` | Запуск relay-симуляции |
| `Benchmark.Compare` | `EntityFX.MqttY.Benchmark.Compare/Program.cs` | Reference-бенчмарк seq/par |

## 9. Правила коммитов

- Коммитьте только свои файлы; не трогайте чужие незакоммиченные правки.
- `results/` не коммитится; `bin/` и `obj/` исключены.
- Числа из бенчмарка приводите с указанием CPU/числа физических ядер, числа повторов и медианы.

## 10. Документация

- `README.md` — быстрый старт и обзор.
- `doc/architecture.md` — слои, инварианты, тик, MQTT, harness, Web/Designer, статус S0–S5.
- `doc/performance.md` — методика замеров, текущие результаты, фазовый профиль, принятые и отклонённые оптимизации.
- `doc/progress.md` — прогресс плана, остаток в часах и статус гейтов.
- `src/EntityFX.MqttSimulator/EntityFX.MqttY.Web.App/README.md` — API и запуск Web.
- Внешние исследовательские отчёты и данные: `github.com/EntityFX/mqtty-meta`.
