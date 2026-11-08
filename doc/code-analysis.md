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
| 7 | `GetPendingMessages().Any()` / `.GetPendingAcknowledgements().FirstOrDefault()` — `ToArray()` копирует список под локом на каждой публикации QoS 2 | **подтверждено (код)** | `StorageExtensions.GetPendingMessages` / `GetPendingAcknowledgements` делают `ToArray()` под `lock`. Горячий путь `MqttBroker.ProcessFromClientPublishCore` (QoS ExactlyOnce) вызывает их на каждой публикации. Добавлены allocation-free `AnyPendingMessage` / `FindPendingAcknowledgement` (перечисление под локом без копии). |
| 8 | Статический `ConcurrentBag<NodeMonitoringPacket> _packetPool` на все узлы → кросс-воркерная争奪 | **отклонена** | Замена на per-node `ConcurrentStack` дала регрессию: `ConcurrentBag` с thread-local списками быстрее для паттерна «один поток пушит/попит», чем `ConcurrentStack` с interlocked на каждую операцию. Измерения: refresh par 1.1844 с (до) vs 1.3783 с (после ConcurrentStack). Возвращено к статичному `ConcurrentBag`. |
| 9 | `ConcurrentDictionary<long, ResponseMonitoringPacket> _responseMessages` на узле | **не изменено** | Доступ из `Send` API (вне Refresh) и из `Refresh` — требует конкурентной безопасности. `ConcurrentDictionary.TryGetValue/TryRemove` — однокорзинный лок, уже дешево. `.Count` заменено на `_responseCount` (Interlocked int). |
| 10 | `ProcessReady` deferred-листы `List.Add` / `Clear` на каждом тике | **не изменено** | `deferred.Clear()` не сбрасывает ёмкость, ссылки меняются местами (swap) — ёмкость сохраняется между тиками. Реаллокации происходят только при росте. |

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

| Приоритет | Работа | Статус | Ожидание |
|---|---|---|---|
| P0 | H1: `AnyPendingMessage` — убрать `ToArray()` в `GetPendingMessages().Any()` (QoS ExactlyOnce hot path) | **выполнено** (`StorageExtensions.cs` + `MqttBroker.cs`) | −0.05…0.15 α при QoS 2 трафике |
| P0 | H2: `FindPendingAcknowledgement` — убрать `ToArray()` в `GetPendingAcknowledgements().FirstOrDefault()` | **выполнено** (`StorageExtensions.cs` + `MqttBroker.cs`) | −0.02…0.05 α при QoS 2 трафике |
| P0 | H3: Per-node packet pool (`ConcurrentStack` вместо `ConcurrentBag`) | **отклонена** | Измерения показали регрессию (1.1844 с → 1.3783 с refresh par). `ConcurrentBag` с thread-local быстрее для паттерна «один поток пушит/попит» |
| P0 | H4: `Dictionary` вместо `ConcurrentDictionary` для `_responseMessages` | **не выполнено** | Доступ из `Send` API вне Refresh требует конкурентной безопасности; выигрыш маржинален |
| P1 | H5: Адаптивный SpinBarrier (экспоненциальный бэк-оф) | **не выполнено** | b 10 → <5 мкс/тик |
| P2 | H6: Federation `Pump()` — пропуск пустых очередей, батч декремент `_activeCount` | **не выполнено** | Modest, ~5–10% времени федерации |
| P2 | H7: `FederationDeliveryLedger` — поиск публикации по SHA-хэшу | **не выполнено** | Снижение накладных расходов на доставку |
| P3 | 8+ физических ядер | **не применимо к коду** | на 16 physical идеальный предел large 6.28× |

### Результаты измерений (LB, 4 воркера, measure 3 с, median из 3, QoS=2 / ExactlyOnce)

| Метрика | До H1/H2 | После H1/H2 | Изменение |
|---|---|---|---|
| seq wall | 4.1948 с | 3.8191 с | **−9.0%** |
| refresh seq | 3.9340 с | 3.5908 с | **−8.7%** |
| par wall | 2.1558 с | 2.1046 с | **−2.4%** |
| refresh par | 1.9034 с | 1.7991 с | **−5.5%** |
| GC allocated | 6128 MB | 5895 MB | **−3.8%** |

> **Вывод:** H1/H2 (`AnyPendingMessage` / `FindPendingAcknowledgement` без `ToArray()`) дают измеримый эффект именно при QoS 2 (ExactlyOnce) трафике: seq ускоряется на ~9%, par — на ~2–5%, аллокации снижаются на ~4%. При QoS 1 (дефолт) эффект не проявляется, т.к. горячий путь публикации не вызывает `GetPendingMessages().Any()`.

## Что из исходного анализа неверно

## План дальнейших работ (для следующего агента)

### Завершённое (не трогать)

| Гипотеза | Что сделано | Результат (QoS=2, 4 воркера, median 3) |
|---|---|---|
| H1/H2 | `AnyPendingMessage` / `FindPendingAcknowledgement` без `ToArray()` | seq −9.0%, par −5.5%, GC −3.8% |
| H3 | per-node `ConcurrentStack` вместо `ConcurrentBag` | **отклонена**: регрессия +16% refresh par |
| M1 | `ResolveBarrier` auto spin/monitor | готово, в коде |
| M2 | serial publish, `nodeChunk=1` | готово, в коде |

### Оставшиеся гипотезы (P1 → P3)

#### P1 — H5: Адаптивный SpinBarrier (снижение b)
- **Место:** `.../Network/TickWorkerPool.cs`, `SpinBarrier.SignalAndWait()` (строки ~427–438)
- **Проблема:** Жёсткий порог `if (spin++ < 4096) SpinWait(32); else Yield();`. При дисперсии затрат на узел быстрые воркеры спин-ждают медленного. Нет адаптации под смещение фаз.
- **Фикс:** Адаптивный spin с измерением времени ожидания за N тиков + экспоненциальный бэк-оф (32→64→128…), затем `Yield`/`Sleep(0)`.
- **Ожидание:** b: 10 → <5 мкс/тик. `WaitForNextGeneration` 31% → ~20%, `SpinBarrier` 19% → ~10%.
- **Проверка:** `MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 ... --engines lb --mode par --workers 4 --no-export`. Сравнить `barrier sum_ms`, `barrier_each`, `p2_parallelism`, `us_per_tick`.
- **Файл:** `TickWorkerPool.cs`

#### P2 — H6: Federation `Pump()` — пропуск пустых очередей, батч декремент
- **Место:** `.../Federation/Core/StaticFederator.cs`, `Pump()` (~строки 78–88)
- **Проблема:** Цикл по всем целям каждый тик; `HasPendingPublish` для пустых очередей; `Interlocked.Decrement` на каждое сообщение.
- **Фикс:** 1) Пропускать `queues[key].Count == 0`. 2) Батч `Interlocked.Add(ref _activeCount, -count)` в конце тика.
- **Ожидание:** ~5–10% времени федерации.
- **Проверка:** `--engines fed --mode both --workers 4 --no-export`. Сравнить wall time.
- **Файл:** `StaticFederator.cs`

#### P2 — H7: `FederationDeliveryLedger` — поиск публикации по SHA-хэшу
- **Место:** `.../Federation/Delivery/FederationDeliveryLedger.cs`, `Find()` (~84–95) и `Receive()` (~97–135)
- **Проблема:** `Find(payload)` читает payload дважды (SHA уже вычислен, но не используется для индекса).
- **Фикс:** `ConcurrentDictionary<string, FederationPublication>` по SHA-256 для O(1) поиска.
- **Ожидание:** снижение накладных расходов на доставку.
- **Проверка:** запуск `--engines fed --mode both --workers 4 --no-export`, замер времени ledger.
- **Файл:** `FederationDeliveryLedger.cs`

#### P2 — H4: `Dictionary` вместо `ConcurrentDictionary` для `_responseMessages`
- **Место:** `.../Network/Node.cs`, строка ~25
- **Проблема:** `ConcurrentDictionary` оплачивает хеширование + блокировки корзин; внутри узла refresh однопоточен.
- **Фикс:** `Dictionary<long, ResponseMonitoringPacket>` per node.
- **Ожидание:** −0.02…0.06 α.
- **Риск:** средний — `Send`/`WaitResponse` могут вызываться вне refresh. Проверить все точки вызова.
- **Проверка:** интеграционные тесты `BrokerMeasurementTests`, `MqttLongConfTests`.
- **Файл:** `Node.cs`

#### P3 — 8+ физических ядер (аппаратное)
- На текущем 4C/8T потолок ~2.5–3×. На 8+ физических — 6–8×. Не требует изменений кода.

### Формула масштабирования (ориентир)

```
Speedup(N) ≈ N / (α + N·(b + h) / T_tick)
```

Текущие: α ≈ 1.51, b ≈ 10 мкс, h ≈ 2 мкс. Цель: α → ~1.1–1.2, b → <5 мкс. Даст ~3–3.5× на 4 ядрах, 5–6× на 8 физических.

### Приёмка после каждого этапа (G1–G6)
1. `dotnet build .../Benchmark.Compare.csproj -c Release --nologo` — 0 ошибок
2. `dotnet test .../EntityFX.Tests.Integration/... -c Release --nologo` — 388/388, 0 падений
3. Замер: `--engines lb --mode both --repeat 5 --warmup-runs 1 --measure 3 --workers 4 --no-export`
4. Сравнить `refresh-stats`, `worker-stats`, `us_per_tick`, GC до/после. Регрессия → откат.

### Команды замеров
```bash
# Базовый замер (LB, 4 воркера, QoS=2)
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 \
  dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build -- \
  --engines lb --mode both --repeat 5 --warmup-runs 1 --measure 3 --workers 4 --qos 2 --no-export

# Federation замер
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 \
  dotnet run --project .../Benchmark.Compare.csproj -c Release --no-build -- \
  --engines fed --mode both --repeat 3 --warmup-runs 1 --measure 3 --workers 4 --no-export

# Кривая масштабирования (1/2/4/8 logical)
for n in 1 2 4 8; do
  DOTNET_PROCESSOR_COUNT=$n dotnet run --project .../Benchmark.Compare.csproj -c Release --no-build -- --engines lb --mode both --no-export
done

# Интеграционные тесты
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo
```

### Список файлов для изменений (осталось)

| Приоритет | Файл | Гипотеза |
|---|---|---|
| P1 | `.../Network/TickWorkerPool.cs` | H5 адаптивный SpinBarrier |
| P2 | `.../Federation/Core/StaticFederator.cs` | H6 Pump оптимизация |
| P2 | `.../Federation/Delivery/FederationDeliveryLedger.cs` | H7 поиск по SHA-хэшу |
| P2 | `.../Network/Node.cs` | H4 `Dictionary` для `_responseMessages` |
