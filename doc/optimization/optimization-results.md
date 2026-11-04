# Отчёт о результатах оптимизации MqttY (optimization-results.md)

**Дата:** 2026-11-01
**Платформа:** .NET 6 (SDK 9.0.319 для сборки), Release
**Методика:** dotnet-trace (cpu-sampling) → speedscope → self-time агрегация ([tools/analyze-speedscope.ps1](tools/analyze-speedscope.ps1)); wall-clock — [tools/measure-scenarios.ps1](tools/measure-scenarios.ps1), 3 прогона

## Итоговая таблица «до / после»

| Сценарий | Baseline | Финал (A2+E) | Итерация 2 | **Итерация 3** | Ускорение vs baseline |
|----------|----------|---------------|------------|----------------|----------------------|
| LoadBalancing (3 брокера, 12 publishers, QoS1) | 9.28 с | 1.96 ± 0.10 с | 1.07 ± 0.01 с | **0.74 ± 0.10 с** | **12.5×** ✅ |
| Federation (ring, 1000 RPS, QoS1) | 6.25 с | 1.24 ± 0.03 с | 0.98 ± 0.03 с | **1.03 ± 0.10 с** | **6.1×** ✅ |
| MqttRelay (original, 131 repeats) | 24.81 с | 9.77 ± 0.19 с | 7.62 ± 0.33 с | **7.06 ± 0.59 с** | **3.5×** ✅ |

## Итерация 3 — сильный раунд по профилю (2026-11-04)

Профиль MqttRelay (dotnet-trace, 15 с): топ-2 остаточные точки — `CounterGroup.Refresh` 8.3 с CPU
(обход всех счётчиков на каждый тик) и инфраструктура `Parallel.ForEach` ~25% CPU
(TaskReplicator, loop-state, барьеры).

- **CounterClock**: новый `Counter/CounterClock.cs` — симулятор публикует (tick, step) один раз
  за тик; `GenericCounter`/`ValueCounter` читают глобальные часы; `CounterGroup.Refresh` больше
  не обходит дерево счётчиков. Устранён топ-2 профиля.
- **Range worker pool**: `NetworkSimulator.RunRanges` — `Parallel.ForEach` заменён фиксированным
  пулом задач с непрерывными диапазонами (локальность кэша, нет TaskReplicator); шард-стратегия
  «сеть + её узлы» сохранена.
- Валидация: интеграционные тесты 388/388; замеры results/final-measurement-strong.txt.

## Итерация 2 — консолидация Engine + параллелизм (2026-11-04)

План: [plans/parallel-optimization-and-engine-consolidation.md](../../plans/parallel-optimization-and-engine-consolidation.md).

### Реализованные изменения

| Этап | Изменение |
|------|-----------|
| 0 | Архитектуры Relay/Federation/LoadBalancing перенесены в `EntityFX.MqttY.Engines` (namespace сохранены; зависимости инвертированы: Apps → Engines; в Apps остались Program.cs + Cli) |
| 1 | Кэш снапшота `Network.Counters` (инвалидация по `_nodesVersion`); гейтинг `Counters.Refresh` по `EnableCounters` в `Network.Refresh`, `NodeBase.Refresh`, `ApplicationBase.Refresh` |
| 2 | Параллельный Refresh использует кэшированные ordered-массивы вместо `ConcurrentDictionary`; порог `ParallelThreshold` (32) — малые топологии всегда серийные; `MaxDegreeOfParallelism`; стратегия 1 — шард «сеть + её узлы на одном потоке» |
| 3 | Lock-free fast-fail проверки capacity в `Network.TryEnqueue` до захвата admission-лока |
| 4 | Кэш предсортированного снапшота подписок брокера (инвалидация при subscribe/disconnect/clean-session) вместо полного скана на каждую публикацию; `PacketIdProvider` — lock-free CAS |
| 5 | `measure-scenarios.ps1`: фикс получения ExitCode; path-finder'ы подтверждены потокобезопасными |

### Валидация

- Интеграционные тесты: 388/388; Web-тесты: 51/51.
- Детерминизм: порядок fanout сохранён (снапшот отсортирован, фильтрация стабильна).
- Замеры: results/final-measurement-stage14.txt, 3 прогона.

## Реализованные изменения (данная итерация)

### Этап A — Профилирование (profiling-report)
- Инструменты: `dotnet-trace collect` + `convert --format speedscope` + собственный анализатор self-time.
- Ключевое открытие (гипотеза H6): в CPU доминировали **LINQ-геттеры счётчиков**:
  - [`NodeCounters.get_Counters`](src/EntityFX.MqttSimulator/EntityFX.MqttSimulator/Counter/NodeCounters.cs) — `.ToArray()` на каждый Refresh каждого узла;
  - [`MqttCounters.get_Counters`](src/EntityFX.MqttSimulator/EntityFX.MqttY.Plugin.Mqtt/Counter/MqttCounters.cs) — 12 LINQ-массивов на каждый тик каждого брокера/клиента;
  - [`NetworkSimulatorCounters.get_Counters`](src/EntityFX.MqttSimulator/EntityFX.MqttSimulator/Counter/NetworkSimulatorCounters.cs) — `Concat().ToArray()`.
- Профиль до A2: LoadBalancing CPU self 128 с (счётчики ~17 с), MqttRelay — 624 с (счётчики ~79 с).

### Этап A2 — Кэширование Counters-коллекций (H6)
- Кэш-массивы с инвалидацией при мутациях (`AddCounter`, `WithNetworks`, сеттеры).
- Эффект: CPU self-time LoadBalancing 128→56 с; wall-clock ×1.5–1.9 к состоянию до A2.

### Этап E — H4 Busy-wait / ожидание ответов
- [`NodeMonitoringPacket.WaitIsReleased`](src/EntityFX.MqttSimulator/EntityFX.MqttSimulator/Network/NodeMonitoringPacket.cs) и [`ResponseMonitoringPacket.WaitIsSet`](src/EntityFX.MqttSimulator/EntityFX.MqttSimulator/Network/ResponseMonitoringPacket.cs): игнорировали параметр `timeSpan` и жёстко ждали `FromMinutes(1)` через kernel `ManualResetEventSlim.Wait`.
- Реализован bounded SpinWait с фолбэком `Wait(timeSpan)` — исходная семантика сохранена, ядро не монополизируется (первая версия с чистым SpinWait меняла число delivery-квитанций и была отклонена).
- Дополнительно: `FederationRunner.Refresh` теперь включает внутреннее исключение в `InvalidOperationException` (диагностика).

### Снятые этапы (с обоснованием)
| Этап | Причина снятия |
|------|----------------|
| B (ObjectPool NetworkPacket) | `NetworkPacket<TContext>` — **record struct**: ObjectPool для value type неприменим, boxing происходит только при пересечении границы `INetworkPacket` |
| C (ArrayPool<byte> для payload) | Fanout брокера раздаёт один и тот же `byte[]` payload нескольким подписчикам — владение неоднозначно, риск возврата в пул при живых ссылках |
| D (кэш IsQuiescent) | После A2/E не входит в топ CPU; риск устаревания кэша выше потенциального выигрыша |
| F (узкое место MqttRelay) | Профиль показал распределённую содержательную работу (QoS1 handshake на 131×N публикаций); дешёвых побед нет |

## Валидация

- **Тесты:** EntityFX.Tests.Integration — 375/378; EntityFX.Tests.Web — 51/51.
- 3 падения — pre-existing флейки federation-тестов (тот же набор падает на чистом HEAD без моих изменений: 42/45): конкурентный `Dictionary` в `DijkstraPathFinder.GetPath` при diagnostics-захвате и расхождение `delivery-receipts.json`. Требуют отдельного бага (не связаны с данной итерацией оптимизации — race существует на HEAD).
- Детерминированность: sealed-тесты scientific-файлов проходят в зелёных прогонах; первая (отклонённая) версия SpinWait меняла число квитанций — отклонена по критерию точности.

## Ограничение: MqttRelay (original) 2.54× вместо 3–5×

Остаточное время — содержательная работа движка MQTT (QoS1 handshake, fanout, ProcessFromClientPublish/FanOut — ~40 с CPU суммарно) при фиксированных 131 повторах. Дальнейший рост требует пересмотра модели брокера (вне рамок инвариантов плана).

## Гипотезы (итог)

| Гипотеза | Статус |
|----------|--------|
| H1 (мониторинг) | ✅ Подтверждена (этап 2, до этой итерации) |
| H2 (последовательный Refresh) | ✅ Подтверждена (этап 3) |
| H3 (очереди) | ⚠️ Частично (этапы 4–5) |
| H4 (busy-wait) | ✅ Подтверждена (этап E: игнор таймаута + kernel-wait) |
| H5 (GC/аллокации) | ⚠️ Частично; для NetworkPacket снята (record struct) |
| **H6 (Counters LINQ)** | ✅ **Подтверждена — главный вклад итерации** |

## Инструменты (добавлены в репозиторий)

- [tools/measure-scenarios.ps1](tools/measure-scenarios.ps1) — воспроизводимое измерение wall-clock 3 сценариев.
- [tools/analyze-speedscope.ps1](tools/analyze-speedscope.ps1) — агрегация self-time по speedscope-профилю.
