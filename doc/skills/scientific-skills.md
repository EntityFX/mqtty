# Scientific Skills Framework (MqttY)

Компетенции, необходимые для работы над научной и измерительной частью проекта MqttY: моделирование дискретно-событийных систем, экспериментальный дизайн, статистическая обработка, бенчмаркинг производительности, воспроизводимость.

Источник: `doc/analysis/message-scaling/`, `doc/performance.md`, `doc/architecture.md`, `AGENTS.md`, тестовые наборы `EntityFX.Tests.Integration` / `EntityFX.Tests.Web`.

## Состав навыков

| Навык | Область | Уровень | Проверка |
|-------|---------|---------|----------|
| **S1** Дискретно-событийное моделирование | Ядро симуляции | Junior → Senior | Запуск Integration-тестов, проверка детерминизма seq/par |
| **S2** MQTT-протокол и профили брокеров | Доменная область | Junior → Senior | `MqttProtocolTests`, `BrokerProfileTestData`, калибровка |
| **S3** Экспериментальный дизайн и статистика | Научная методология | Middle → Senior | `results.json`, `measurements.csv`, holdout-ошибка |
| **S4** Измерения производительности и профилирование | Performance engineering | Middle → Senior | `Benchmark.Compare`, `MQTTY_REFRESH_STATS`, гейты G1–G6 |
| **S5** Воспроизводимость и целостность данных | Научная этика | Middle → Senior | `SchemaVersion`, golden-трассы, SHA-256, run_campaign.py |
| **S6** Federation и Load Balancing domain | Движки LB / Federation | Middle → Senior | Federation-тесты, лимиты (brokers ≤16, pub×recv ≤1M) |

Смежный навык: **dotnet-performance** (`doc/skills/dotnet-performance.md`) — низкоуровневая оптимизация .NET, барьеры, пулы воркеров, GC.

---

## S1 — Дискретно-событийное моделирование (DES)

**Что должен знать участник:**
- Разница между модельным временем (tick) и wall-временем.
- Порядок фаз одного Refresh: `PublishClock → RefreshTopology (сеть → fence → узел → fence) → CompleteTick`.
- Инварианты: сеть не обрабатывает пакет, отправленный узлом в том же тике; узел не обрабатывает доставку до завершения тика сетью.
- Quiescence: сеть и все узлы пусты → симуляция может завершиться (drain).
- Детерминизм: один seeded RNG, сортировка экспортируемых коллекций, сид в каждом сэмпле, запрет `DateTime.Now` / `Guid.NewGuid` в модели.

**Где проверяется в коде:**
- `src/.../Network/NetworkSimulator.cs` — `Refresh(bool parallel, int strategy)`, `TickPipeline`.
- `src/.../Network/SimulationClock.cs`, `TickContext.cs`.
- `src/.../Network/TickWorkerPool.cs` — фазы, барьер, чанки.
- Тесты: `DeterministicCoreTests`, `EngineLifecycleTests`, `FederationDeterminismTests`.

**Валидация навыка S1:**
```
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo
# Ожидание: 436/436 пройдено (подтверждено запуском 08.11.2026)
```
При несовпадении seq vs par (`core=MISMATCH`) навык S1 не пройден — требуется разбор порядка фаз.

## S2 — MQTT-протокол и профили брокеров

**Что должен знать участник:**
- Семантика QoS 0 / 1 / 2: PUBLISH, PUBACK, PUBREC, PUBREL, PUBCOMP; поток пакетов клиент→брокер→получатель.
- Сессия: pending-списки, подписки (topic filters), retained messages, last will.
- Профиль брокера из наблюдений MqttBenchmark: задержки, пропускная способность, потери.
- Соответствие клиентов и сетей: издатель (publisher), получатель (receiver), брокер, служебные клиенты federator/relay.

**Где проверяется в коде:**
- `src/.../Plugin.Mqtt/` — сессии, кодек, admission, метрики.
- `src/.../MqttRelay.App/` — профили брокеров, BrokerFidelity.
- Тесты: `MqttProtocolTests`, `MqttTests`, `MqttStagedTests`, `BrokerMeasurementTests`, `EmpiricalFidelityTests`.

**Валидация навыка S2:**
```
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo --filter "FullyQualifiedName~MqttProtocol"
# Ожидание: все MQTT-тесты пройдены
```

## S3 — Экспериментальный дизайн и статистика

**Что должен знать участник:**
- Контролируемые переменные: сид, топология, RPS, размер полезной нагрузки, QoS, количество тиков измерения.
- Прогрев ≥ 1 прогона, затем медиана из ≥ 5 повторов; чередование seq/par.
- Amdahl's law: `S(n) = 1 / ((1-p) + p/n)`; фиксированные расходы ограничивают ускорение.
- CV (коэффициент вариации) > 5 % — повторить серию; шум на текущем хосте ±3–5 %.
- Hold-out валидация, регрессия (R², RMSE), корреляции (log B, log P, log X).
- Ограничения: payload 16 байт; Federation \(B\le16\); профили с потерями требуют отдельной серии. QoS 0/1/2 и seed 1/7/42 проверены в `robustness.csv`.

**Где проверяется в коде:**
- `doc/analysis/message-scaling/article.md`, `topologies.md`, `measurements.csv`, `results.json`.
- Скрипты: `run_campaign.py`, `analyze.py`.
- `doc/performance.md` §1 — протокол замеров, гейты.

**Валидация навыка S3:**
```
python3 doc/analysis/message-scaling/analyze.py
# Ожидание: R² 0.9946–0.9999998, holdout-ошибка в пределах заявленных
```

## S4 — Измерения производительности и профилирование

**Что должен знать участник:**
- Протокол reference-бенчмарка: `--engines lb,relay,fed --repeat 5 --warmup-runs 1 --measure 3 --drain 10 --workers N --no-export`.
- Постоянный пул воркеров `TickWorkerPool` вместо `Task`/`Parallel.For` на каждый тик.
- Двухфазный refresh (сеть → fence → узел → fence); стратегия 0 (flat two-phase) всегда параллельная, стратегия 1 — последовательная.
- Барьеры: spin при `workers ≤ physical cores`, monitor при SMT-подписке; `--workers 24` на 12 физических ядрах — катастрофа для LB.
- Диагностика: `MQTTY_REFRESH_STATS=1`, `MQTTY_WORKER_STATS=1`, `MQTTY_NODE_STATS=1`, `MQTTY_PHASE_STATS=1`.
- GC: ServerGC + ConcurrentGC=false дляベンчмарка и CLI; полный GC вне измеряемого участка между сэмплами.

**Где проверяется в коде:**
- `src/.../Benchmark.Compare/Program.cs`.
- `src/.../Network/NetworkSimulator.cs`, `TickWorkerPool.cs`.
- `doc/performance.md` §1–§7 — протокол, результаты, диагностические переключатели.
- `doc/skills/dotnet-performance.md` — правило 0 (сначала измерение).

**Валидация навыка S4:**
```
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build -- \
  --engines lb --repeat 5 --warmup-runs 1 --measure 3 --drain 10 --workers 12 --brokers 64 --nets 4 --publishers 256 --rps 20000 --no-export --no-strict
# Ожидание: wall speedup ~5.0×, refresh speedup ~6.0×, core=match
```

## S5 — Воспроизводимость и целостность данных

**Что должен знать участник:**
- Детерминистский вывод: сортировка экспортируемых коллекций, сид в сэмпле, нет случайности в модели.
- Версия схемы результатов (`SchemaVersion`) и уплотнение (seal) при изменении экспортируемой схемы.
- Golden-трассы S0 (NDJSON send/receive), `CharacterizationPacketTraceTests`.
- SHA-256 хеш полезной нагрузки (один раз на публикацию, не на каждую пересылку).
- Кампании измерений: `measurements.csv` (63), `large-topologies.csv` (13), `robustness.csv` (18); `results.json` (коэффициенты, R², RMSE, корреляции).
- Сравнение хостов: `hardware-validation.csv` (Celeron N3350 vs Xeon E5-2680 v3).
- Сводка и выводы по топологиям: `topologies-summary.md`.

**Где проверяется в коде:**
- `src/.../Results/JsonResultWriterTests.cs`, `VersionedPersistenceTests.cs`.
- `src/.../Network/PacketTrace.cs`.
- `doc/analysis/message-scaling/` — Приложение A (воспроизводимость), Приложение B (схема данных).

**Валидация навыка S5:**
```
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Web/EntityFX.Tests.Web.csproj -c Release --nologo
# Ожидание: 62/62 пройдено (подтверждено запуском 08.11.2026)
```

## S6 — Federation и Load Balancing domain

**Что должен знать участник:**
- Три топологии федерации: star (полный граф пересылок, O(B²) трафик), ring (O(B)), line (O(B)).
- Federation Delivery Ledger: `ConcurrentDictionary` публикаций + per-receiver lock; `Attempted`, `Expected`, `Unique`, `FullyDelivered`.
- `StaticFederator.Pump`: параллельный на пуле тика, `IsIdle` кэширован, `QueueDrops`, `HopLimitDrops`, подавление дублей (`seen`).
- Лимиты Federation: brokers ≤ 16, публикаций ≤ 100 000, `publications × receivers ≤ 1 000 000`.
- Federation недетерминирован по receiver-латентности и `queueDrops` при перегрузке; core-метрики (counts, unique, goodput, publish p50–p99) совпадают.
- Load Balancing: DirectBalanced, Round Robin, Least Connections; изоляция сетей клиентов (`MQTTY_ISOLATE_NETWORKS`).

**Где проверяется в коде:**
- `src/.../Engines/Federation/` — `FederationDeliveryLedger.cs`, `StaticFederator.cs`, `FederationTopology.cs`.
- `src/.../Engines/LoadBalancing/` — `LoadBalancingTopologyBuilder.cs`, раннеры.
- Тесты: `FederationResultWriterTests`, `FederationDiagnosticsTests`, `LoadBalancingDemoTests`.

**Валидация навыка S6:**
```
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo --filter "FullyQualifiedName~Federation|FullyQualifiedName~LoadBalancing"
# Ожидание: все тесты федерации и балансировки пройдены
```

## Матрица валидации навыков (прогон)

| Навык | Команда валидации | Ожидаемый результат | Статус 08.11.2026 |
|-------|-------------------|---------------------|-------------------|
| S1 | Integration-тесты (детерминизм) | 436/436, core=match | ✅ Подтверждён |
| S2 | MQTT-тесты Integration | все MQTT-тесты пройдены | ✅ Подтверждён |
| S3 | `analyze.py` | R² 0.9946–0.9999998 | ✅ Данные согласуются |
| S4 | Benchmark.Compare (LB L) | wall 4.76× CI [4.48,4.91], refresh 5.72× CI [5.37,5.84], core=match | ✅ Подтверждено repeat 9 |
| S5 | Web-тесты | 62/62, JSON-результаты, SchemaVersion | ✅ Подтверждён |
| S6 | Federation + LB тесты | все пройдены, лимиты соблюдены | ✅ Подтверждён |

## Правила присвоения уровней

- **Junior:** может запускать валидационные команды и интерпретировать green/red; понимает порядок фаз и QoS.
- **Middle:** может модифицировать топологию или сценарий, запускать кампанию измерений, интерпретировать R²/CV, вносить неломающие изменения в код.
- **Senior:** может добавлять новый двигатель или топологию, изменять порядок фаз с сохранением детерминизма, утверждать гейты G1–G6, ревьюировать научную обоснованность изменений.

---

---