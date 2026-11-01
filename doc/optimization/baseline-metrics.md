# Метрики производительности MqttY (ход оптимизации)

**Дата:** 2026-11-01  
**Платформа:** .NET 6 (SDK 6.0.428), Release сборка  
**Оборудование:** локальная рабочая станция

## Базовая линия (до оптимизации)

| Сценарий | Команда | Конфигурация | Wall-clock | Статус |
|----------|---------|-------------|------------|--------|
| LoadBalancing | `demo --brokers 3 --mode roundRobin --publishers 12 --qos 1 --seconds 1 --seed 42` | 3 брокера, roundRobin, 12 publishers, QoS 1, 1 сек моделирования | 9.28 с | completed |
| Federation | `demo --brokers 3 --topology ring --mode federation --publishers 12 --qos 1 --rps 1000 --seconds 0.2 --seed 42` | 3 брокера, ring, federation, 12 publishers, QoS 1, 1000 RPS, 0.2 сек | 6.25 с | completed |
| MqttRelay (original) | `original` | 4 брокера, 15 клиентов, 131 repeats, refreshStrategy=0, 8 байт, TickPeriod=0.1ms | 24.81 с | completed |

## После Этапа 2 (мониторинг выключен)

| Сценарий | Wall-clock | Ускорение vs baseline |
|----------|------------|----------------------|
| LoadBalancing | 6.85 с | 1.36× |
| Federation | 4.30 с | 1.45× |
| MqttRelay (original) | 21.52 с | 1.15× |

## После Этапа 3 (параллельный Refresh, стратегия 1)

| Сценарий | Wall-clock | Ускорение vs baseline |
|----------|------------|----------------------|
| LoadBalancing | 4.83 с | 1.92× |
| Federation | 2.81 с | 2.22× |
| MqttRelay (original) | 20.09 с | 1.23× |

## После Этапа 4 (кэширование Nodes в Network)

| Сценарий | Wall-clock | Ускорение vs baseline |
|----------|------------|----------------------|
| LoadBalancing | 4.75 с | 1.95× |
| Federation | 2.48 с | 2.52× |
| MqttRelay (original) | 16.90 с | 1.47× |

## После Этапа 5 (Object Pooling, guard Monitoring.Push, Step per transfer)

| Сценарий | Wall-clock | Ускорение vs baseline |
|----------|------------|----------------------|
| LoadBalancing | 4.36 с | 2.13× |
| Federation | 2.65 с | 2.36× |
| MqttRelay (original) | 16.90 с | 1.47× |

## После Этапа E (H4: SpinWait в WaitIsSet/WaitIsReleased)

`WaitIsSet(TimeSpan)`/`WaitIsReleased(TimeSpan)` игнорировали параметр `timeSpan` и жёстко ждали
`TimeSpan.FromMinutes(1)` через `ManualResetEventSlim.Wait` (kernel-переход). Заменено на bounded
SpinWait с фолбэком на `Wait(timeSpan)` (исходная семантика сохранена; гибрид не монополизирует ядро).

Валидация: federation-тесты 43/45 (на чистом HEAD — 42/45; падения pre-existing:
DijkstraPathFinder concurrent Dictionary в diagnostics, delivery-receipts).

| Сценарий | Wall-clock (среднее 3 прогонов) | Ускорение vs baseline |
|----------|--------------------------------|----------------------|
| LoadBalancing | 1.95 с | **4.76×** |
| Federation | ~1.19 с (медиана; прогрев 0.55–1.23) | **~5.25×** |
| MqttRelay (original) | 10.14 с | **2.45×** |

## После Этапа A2 (H6: кэширование Counters-коллекций)

Профилирование (dotnet-trace cpu-sampling, tools/analyze-speedscope.ps1) выявило доминанту:
`CounterGroup.Refresh` → LINQ-геттеры `NodeCounters.get_Counters` (`.ToArray()` на каждый Refresh
каждого узла), `MqttCounters.get_Counters` (12 LINQ-массивов), `NetworkSimulatorCounters.get_Counters`
(`Concat().ToArray()`). Кэш с инвалидацией при мутации.

| Сценарий | Wall-clock (среднее 3 прогонов) | Ускорение vs baseline | Ускорение vs Этап 5 |
|----------|--------------------------------|----------------------|--------------------|
| LoadBalancing | 2.82 с | **3.29×** | 1.55× |
| Federation | 1.37 с | **4.56×** | 1.93× |
| MqttRelay (original) | 11.04 с | **2.25×** | 1.53× |

## Целевой прирост

- **Цель:** 3–5× ускорение wall-clock времени
- **Достигнуто (после этапов 2–5):** 2.13-2.36× (LoadBalancing, Federation), 1.47× (MqttRelay)
- **Достигнуто (финал, после A2+E):** см. [optimization-results.md](optimization-results.md):
  - LoadBalancing: **4.73×** (1.96 ± 0.10 с vs 9.28 с) ✅
  - Federation: **5.04×** (1.24 ± 0.03 с vs 6.25 с) ✅
  - MqttRelay (original): **2.54×** (9.77 ± 0.19 с vs 24.81 с) ⚠️
- **Ограничение:** точность симуляции сохраняется (delivery rate, latency quantiles, ошибки — без изменения)

## Гипотезы для проверки (H1–H5)

1. **H1 (Мониторинг):** `NetworkLogger.Push()` доминирует по CPU — **ПОДТВЕРЖДЕНА** ✅
2. **H2 (Последовательный Refresh):** стратегия 0 по умолчанию — **ПОДТВЕРЖДЕНА** ✅
3. **H3 (Очереди):** поэлементное `TryDequeue` — частично ⚠️
4. **H4 (Busy-wait):** циклы `Drive`/`Until` — не реализовано ⏳
5. **H5 (GC):** аллокации объектов на каждый пакет — частично (Object Pool для NodeMonitoringPacket) ⚠️

## Реализованные изменения (все коммиты)

### 1. Отключение мониторинга
- `NetworkLogger.cs`: флаг `_monitoringEnabled`, ранний выход в `Push()`, устранение `lock (_stdLock)` → `ConcurrentDictionary.AddOrUpdate`
- `NetworkSimulator.cs`: свойство `EnableMonitoring` (по умолчанию `false`), обёрнуты `Monitoring.Push/BeginScope/EndScope` в `if (EnableMonitoring)`
- `NetworkSimulationEngine.cs`: установлены `EnableMonitoring = false` и `logger.MonitoringEnabled = false`

### 2. Параллельный Refresh
- `LoadBalancingExperimentRunner.cs`: `graph.Refresh(false, 0)` → `graph.Refresh(true, 1)`
- `FederationRunner.cs`: `topology.Graph.Refresh(false, 0)` → `topology.Graph.Refresh(true, 1)`

### 3. Кэширование Nodes
- `Network.cs`: добавлено кэширование свойства `Nodes` с версионированием (`_nodesCache`, `_nodesVersion`), инкремент `_nodesVersion` в `AddClient/RemoveClient/AddServer/RemoveServer/AddApplication/RemoveApplication`

### 4. Оптимизация Node.Refresh()
- `Node.cs`: кэширование `NetworkSimulator` и `TotalTicks` в локальные переменные, условное обновление счётчиков, удаление `Monitoring.Push` вызовов

### 5. Object Pooling
- `Node.cs`: `ConcurrentBag<NodeMonitoringPacket>` для переиспользования объектов
- `NodeMonitoringPacket.cs`: `Reset()` метод, settable свойства для пула

### 6. Оптимизация Step()
- `NodeBase.cs`: `Step()` инкрементируется на каждую передачу (Send/Receive)
- `Network.cs`: удалён `Step()` из `Refresh()` (был per-tick, не per-transfer)

### 7. Guard Monitoring.Push в Network.cs
- Все `Monitoring.Push()` вызовы обёрнуты в `if (NetworkSimulator.EnableMonitoring)` для пропуска строковой интерполяции
- `INetworkSimulator.cs`: добавлено свойство `EnableMonitoring`

### 8. InMemoryRepository
- `ReadAll()` использует `_elements.Values` вместо LINQ `Select`

### 9. BrokerProcessingQueue
- Переиспользуемый буфер `_readyBuffer`, `PendingItem` как `struct`

## Дальнейшие шаги для достижения 3-5×

1. **Object Pooling для NetworkPacket** — `ObjectPool<NetworkPacket<int>>` в `NodeBase.GetPacket`/`GetContextPacket`
2. **ArrayPool<byte>** для payload пакетов
3. **Оптимизация Until loops** — проверка `IsQuiescent` реже (каждые N тиков)
4. **Профилирование** — запуск `dotnet-trace` для выявления оставшихся горячих точек
5. **Оптимизация MqttBroker/MqttClient** — кэширование `IsQuiescent`, guard `Monitoring.Push` в ack paths