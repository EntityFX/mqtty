# MqttY: глубокое ревью и план переписывания без изменения функциональности

**Дата:** 2026-11-08  
**Объём:** ~38 000 строк C#, 16 проектов, 485 файлов.  
**Метод:** статический разбор всех слоёв + выборочная проверка ключевых утверждений по коду.  
**Цель:** зафиксировать, что именно является контрактом поведения, и предложить переписывание,
которое меняет структуру, но не научные результаты, протокол, порядок тиков и форматы результатов.

---

## 0. Резюме

Система научно состоятельна: есть детерминированное модельное время, профили брокеров,
учёт публикаций/доставок, квантили, воспроизводимые сиды. Но прикладной слой вырос как набор
скриптов вокруг God-object симулятора. Главная проблема — **не количество кода, а отсутствие
явных границ и инвариантов**:

- `NetworkSimulator` (1039 строк) одновременно реестр, часы, планировщик, пул воркеров,
  корень счётчиков, мониторинг и источник packet id.
- `MqttBroker`/`MqttClient` (812/754 строки) одновременно протокол, сессии, fanout, admission,
  метрики, диагностика и codec.
- Пять построителей топологии, четыре-пять harness-циклов, четыре ledger, пять экспортёров,
  шесть-семь парсеров CLI — каждый со своими допущениями.
- Инварианты (порядок фаз, `PassTillNextTick`, quiescence, лаг счётчиков) существуют только
  в комментариях и тестах; их легко нарушить при любой правке.

**Стратегия:** не big-bang, а strangler. Сначала characterization-тесты на наблюдаемое
поведение, затем механическое выделение швов (clock, topology snapshot, tick pipeline),
затем session aggregate и общий experiment harness, затем приложения. Каждый этап
самостоятелен и проверяется сравнением трасс пакетов и метрик «до/после».

---

## 1. Что считать «функциональностью»

«Не менять функциональность» означает сохранить:

1. **Модель времени:** дискретный тик, порядок фаз «сети → барьер → узлы → барьер».
2. **Латентности:** минимум 2 тика на узел→узел, 3 тика на первый удалённый хоп.
3. **Порядок доставки:** FIFO внутри узла/сети, «ready snapshot → deferred → swap».
4. **Квази-покой:** конъюнкция очередей сети, узла, plugin-состояния и (для `ActiveOnly`)
   ожидающих ответов.
5. **Семантику протокола:** QoS 0/1/2, fanout, дедупликация, hop limit, queue capacity.
6. **Определения метрик:** counts, unique, goodput, publish/receiver p50–p99; diagnostics
   (receiver latency, queueDrops) остаются diagnostics.
7. **Форматы результатов** и CLI/HTTP API, включая коды возврата и имена файлов.
8. **Профили брокеров и калибровку** (интерполяция/эмпирические CDF, сиды).

Всё остальное — классы, интерфейсы, структуры данных, DI, потоки — можно менять.

---

## 2. Инварианты, которые обязательно сохранить

Это «золотые» правила, выведенные из кода; их должен pin-ить characterization-набор.

| # | Инвариант | Где закреплён |
|---|---|---|
| I1 | Пакет, созданный в тике `T`, не обрабатывается в том же тике (`PassTillNextTick`) | `Node.cs:177-193`, `Network.cs:525-553` |
| I2 | Отложенный пакет decrement-ится ровно один раз за refresh и не возвращается в `ConcurrentQueue` | `Node.cs:140-175`, `Network.cs:479-515` |
| I3 | В проходе сначала обрабатывается ready-snapshot, затем **предыдущий** deferred-список, затем swap | `Node.cs:126-127`, `Network.cs:493-515` |
| I4 | Ни один узел не refresh-ится, пока идёт refresh сетей | `TickWorkerPool.cs:265-268` |
| I5 | `IsQuiescent` не включает `_responseMessages` (ожидающий ответ граф «спокоен») | `NetworkSimulator.cs:25-27,348-350` |
| I6 | Счётчики обновляются значением тика **до** `Tick()`, т.е. отстают на тик | `NetworkSimulator.cs:425-427` vs `:521` |
| I7 | `RealTime` — время от первого `Refresh`, а не сумма refresh | `NetworkSimulator.cs:297,970` |
| I8 | TTL уменьшается только при enqueue в следующую сеть; локальный TTL не меняется | `Network.cs:331,437-447` |
| I9 | Отказ admission молчаливый (`TryEnqueue=false`), отправитель его игнорирует | `Network.cs:298-337`, `Node.cs:200` |
| I10 | Маршрут резолвится в момент отправки; отключённый адресат продолжает получать | `Network.cs:357-363` |
| I11 | Packet id — единый атомарный счётчик для протокола и транспорта | `Node.cs:259,292`, `NetworkSimulator.cs:1030-1038` |
| I12 | `NetworkPacket<T>` — mutable `record struct`, передаётся как `INetworkPacket` (boxing, identity) | `Contracts/Network/NetworkPacket.cs:6-36` |
| I13 | `CounterClock` процесс-глобален | `Contracts/Counters/CounterClock.cs:9-23` |
| I14 | `Reset()` не no-op: чистит очереди и двигает тик; при ошибке `RefreshWithCounters` вызывает `Reset()` | `NetworkSimulator.cs:644-680,910-925` |
| I15 | `Network.Speed` — байты/с, не биты | `Contracts/Options/NetworkOptions.cs:9-13` |

---

## 3. Карта проблем по слоям

### 3.1 Ядро и транспорт

**God object.** `NetworkSimulator` совмещает реестры (5 индексов), версионирование топологии,
часы, два планировщика refresh, пул воркеров, счётчики, мониторинг, packet id и real-time драйвер
(`NetworkSimulator.cs:28-104,412-547,682-739,1030-1038`). Любая правка затрагивает всё.

**Неявные контракты и гонки.**
- `Server._serverClients` — обычный `Dictionary`, читается из fanout (`MqttBroker.cs:213`) и
  пишется из другого воркера при connect/disconnect (`Server.cs:7,55-75`).
- `Network.AddClient/AddServer/AddApplication` кастуют `IClient/IServer/IApplication` к
  реализациям (`Network.cs:154,258,620`); контракты «приблизительные».
- `Configure` выполняется параллельно с `Refresh` (`NetworkSimulationEngine.cs:59-65`):
  `Network.Link` и `PathFinder.Build` мутируют структуры, которые читают воркеры.
- `NetworkLoggerScope.Items` — `List<>.Add` из всех воркеров (`NetworkLogger.cs:85`).
- `DijkstraPathFinder.Build()` очищает кэш, а конкурентный `GetPath` может записать
  устаревший маршрут после очистки (`DijkstraPathFinder.cs:15-68`).

**Подтверждённые дефекты.**
- `Server.Stop()`/`Application.Stop()` удаляют по `Address`, а `Start()` добавляет по `Name`;
  `Network.RemoveServer` ищет по ключу-имени (`Server.cs:99-110`, `Application.cs:143-155`,
  `Network.cs:248-276`). Объект не удаляется, `IsStarted` остаётся `true`.
- `NetworkCounters.Refresh` для outbound берёт `_inboundCounter.TickFirstValue` и делит без
  проверки нуля (`Counter/NetworkCounters.cs:145-153`).
- `NetworkSimulator.AddClient`: при успешном `_clients.TryAdd` и неуспешном `_nodes.TryAdd`
  клиент остаётся в `_clients` (`NetworkSimulator.cs:775-798`).
- `_responseCount` инкрементится в `PreSend`, но декрементится только в `WaitResponse`,
  не при доставке (`Node.cs:276,321,366`).
- Packet pool: `SendToNetwork` всегда добавляет пакет в `ConcurrentBag`, а `CompleteReceive`
  уважает `MQTTY_NO_PACKET_POOL` (`Node.cs:195-212`). При выключенном пуле bag растёт без границ.
- `Clear()` вызывает `RemoveNetwork` → `UpdateRoutes()` на каждую сеть, т.е. O(N²) teardown
  (`NetworkSimulator.cs:243,941-970`).

**Производительность.** Инфляция работы α≈1.51, барьер b≈10 мкс/тик/воркер, ~50% сэмплов —
ожидание/спин. Это уже измерено; переписывание должно снижать α (аллокации, LINQ, снимки
списков) и b (барьер/хвост), а не переставлять планировщики — scheduling-эксперименты
(pipeline, per-network, cost-aware, parallel Pump) отклонены замерами.

### 3.2 MQTT-плагин

**Нет агрегата сессии.** `ClientSession` — три публичных `List<>` и три lock-объекта
(`Internals/ClientSession.cs:3-31`). Один список смешивает входящий и исходящий потоки,
поэтому packet id двух независимых провайдеров могут совпасть. `ProcessToClientPublishAck`
удаляет первую запись по `PacketId` без фильтра статуса/QoS (`MqttBroker.cs:431-456`) —
PUBACK доставки может удалить входящий QoS2-рекорд, после чего PUBREL не находит запись,
fanout теряется, PUBCOMP всё равно отправляется. Симметрично на клиенте (`MqttClient.cs:522-549`).

**Дублирование FSM.** `Connect/BeginConnect/CompleteConnect`,
`Subscribe/BeginSubscribe/CompleteSubscribe`, `SaveMessage`, `SendPublishAck`,
`SendPublishReceived`, поиск/удаление pending — по две почти одинаковые копии на broker и client.

**Снимки состояния.** `InMemoryRepository.Update` пересобирает весь `_elements.Values.ToArray()`
под локом (`Internals/InMemoryRepository.cs:34-38`), и MQTT вызывает `Update` на каждое
изменение pending (`MqttBroker.cs:453,485,543,585,611,707,747`; `MqttClient.cs` — ещё 9 мест).
Комментарий «rebuild only on rare membership changes» не соответствует коду.

**Неограниченные очереди и потеря сообщений.**
- `PendingAcknowledgements` создаётся на каждый PUBREL/PUBCOMP и не вычищается
  (`MqttBroker.cs:580`, `MqttClient.cs:475`).
- Дубликат QoS2 PUBLISH после завершения снова удаляет dedup-маркер и делает повторный fanout
  (`MqttBroker.cs:304-322,589-590`) — нарушение exactly-once.
- `PendingToSend` записывается и никогда не читается; при неудачном `Send` pending не
  откатывается (`MqttClient.cs:281-300`) — граф может никогда не стать quiescent.

**Аллокации в hot path.** `PublishFormatter`/`MqttEncoder`/`ByteExtensions` строят
`List<byte>` и копируют payload на каждого подписчика; `Split('/')` на каждый матч топика;
`$"Publish {Name} to Subscriber..."` строится даже с `NullNetworkLogger`
(`MqttBroker.cs:343-422`); SHA-256 на каждого подписчика при loss-моделировании.

**Не реализовано, хотя интерфейс обещает:** keep-alive/PING, DISCONNECT-очистка,
UNSUBSCRIBE, retained store и replay, session resume с DUP. Это надо либо реализовать,
либо явно объявить `NotSupported`, чтобы модель не теряла сообщения молча.

### 3.3 Движки, приложения, CLI, бенчмарк

- **«Relay» означает три разные вещи:** `RelayEngine` → `FederationRunner(Mode=Relay)`;
  legacy `MqttRelayApp`; `MqttRelayExperimentRunner`. Reference-бенчмарк тестирует
  legacy-путь (`Benchmark.Compare/Program.cs:62-76`), а не продуктовый движок.
- **Пять построителей топологии, 4–5 harness, 4 ledger, 4–5 экспортёров, 6–7 CLI-парсеров.**
  `Until`/drain-цикл продублирован пять раз; квантовые гистограммы — четыре раза.
- **`EngineRunSettings` не читается** ни одним движком (`*Engine.cs:16`); `SimulationRunOptions.Parallel`
  и `RefreshStrategy` игнорируются native-путём; Web выставляет `Parallel=false`, но это не влияет.
- **Параллельный refresh игнорирует `strategy`:** параметр есть в сигнатуре
  `ParallelRefreshImplementation`, но не используется (`NetworkSimulator.cs:412`). Поэтому
  seq `strategy=1` и par — не один и тот же эксперимент, а бенчмарк именно так их и сравнивает.
- **Ошибки проглатываются:** `RefreshWithCounters` при неудаче делает `Reset()` (стирает граф
  и двигает тик), а `MqttRelayExperimentRunner` игнорирует `bool` (`:167,340,352,366`).
  Итог — статус `Completed` при пустых метриках.
- **Бенчмарк:** Relay-статус захардкожен `"completed"` и не проверяет доставку
  (`Program.cs:74-75`); при `--no-export` не видно, что default-экспорт конфликтует с warmup
  по каталогу; `--workers` мутирует переменную окружения процесса и не восстанавливает.
- **Env-переменные** разбросаны по 18 местам, часть — `static readonly` (нельзя переключить
  в процессе), часть читается на каждом тике. Нет единого `RuntimeOptions`.

### 3.4 Web, Designer, контракты

- **Нет прикладного слоя.** `SimulationsController` с 9 зависимостями делает preflight,
  валидацию, резолв пресета, регенерацию графа, diff, snapshot профилей и enqueue
  (`SimulationsController.cs:44-90`); `TwinValidationsController` — 74 строки в одном try.
- **API-модели = модели хранения.** `SavedTopologyDto`, `SimulationJob`, `SimulationRunOptions`
  мутабельны, без схемы/версии; неизвестные поля молча игнорируются; `RunSettings=null` → NRE.
- **Безопасность:** нет authentication/authorization/CORS/HTTPS/rate limiting/global exception
  (проверено: в `Program.cs` и контроллерах нет соответствующих вызовов). `SettingsController`
  читает/пишет любой файл внутри data root; persisted `OutputDir` не проверяется при выдаче
  результатов (`SimulationsController.cs:190-212`). Это цепочка чтения чужих файлов и
  подмены job-файла.
- **Designer теряет данные:** `QueueSize`/`SendTicks` не сохраняются и заменяются литералами
  (`NetworkDesignModel.cs:25-33,77-87`); `RebuildGraph` сохраняет позиции и тут же перезаписывает
  layout (`NetworkEditorViewModel.cs:222-228`); позиции/zoom вообще не входят в persisted-контракт.
- **Контракты протекают:** `IServiceProvider` в `NodeBuildOptions`/`IAction`; `ScenarioAction`
  и `NetworkSimulation` — реализации в assembly contracts; `NetworkPacket<T>` — mutable struct,
  реализующий mutable-интерфейс; `INetworkSimulator` — 90-строчный god-интерфейс.

### 3.5 Тесты

388 тестов зелёные, но:
- два проекта на разных фреймворках (MSTest/net6 и xUnit/net8), нет `runsettings`/таймаутов;
- xUnit параллелит классы в одном процессе, а `CounterClock`, packet pool и env-флаги —
  глобальные;
- тяжёлые «unit»-тесты (600k refresh, полные научные прогоны) смешаны с быстрыми;
- нет юнит-тестов ядра/контрактов/Designer/репозиториев;
- часть тестов ассертит внутрь колбэка или вообще ничего не ассертит.

---

## 4. Целевая архитектура

```
┌──────────────────────────────────────────────────────────────┐
│ MqttY.Apps.Cli        MqttY.Apps.Web        MqttY.Apps.Designer│
│  (тонкие адаптеры, один парсер, HTTP, MVVM)                   │
└───────────────▲──────────────────────────────▲───────────────┘
                │                              │
┌───────────────┴──────────────────────────────┴───────────────┐
│ MqttY.Experiment                                             │
│  TopologyBuilder · LoadDriver · DrainPolicy · Metrics ·      │
│  ResultWriter (schema+seal) · ExperimentRunner               │
└───────────────▲──────────────────────────────▲───────────────┘
                │                              │
┌───────────────┴──────────┐   ┌───────────────┴──────────────┐
│ MqttY.Engines            │   │ MqttY.Protocol.Mqtt          │
│  LB / Relay / Federation │   │  SessionAggregate + FSM      │
│  routing & delivery      │   │  Codec (Span) · Admission    │
└───────────────▲──────────┘   │  BrokerMetrics               │
                │              └───────────────▲──────────────┘
┌───────────────┴──────────────────────────────┴───────────────┐
│ MqttY.Simulation.Core                                        │
│  SimulationClock/TickContext · TopologyRegistry+Snapshot     │
│  PacketTransport (slot rings, admission) · TickPipeline      │
│  CounterSink · MonitoringSink                                │
└──────────────────────────────▲───────────────────────────────┘
                               │
                    MqttY.Contracts (чистые DTO, без DI)
```

### 4.1 Ключевые интерфейсы

| Интерфейс | Ответственность | Заменяет |
|---|---|---|
| `ISimulationClock` / `TickContext` | `Ticks`, `Steps`, `Advance()`, wall-clock | `_tick`, `ThreadLocal`, `CounterClock` |
| `ITopologyRegistry` / `TopologySnapshot` | неизменяемый снимок сетей/узлов на тик | `_nodes/_networks` + `OrderBy` каждый тик |
| `IPacketTransport` / `IAdmissionPolicy` | dueTick-очереди, TTL, отказ | `ConcurrentQueue` + deferred `List` + `TryEnqueue` |
| `ITickPipeline` | стадии `PublishClock → Networks → Barrier → Nodes → Complete` | 4 копии `RefreshImplementation` |
| `IClientSession` / `ISessionStore` | агрегат сессии, FSM-переходы, inbound/outbound namespaces | `ClientSession` + `InMemoryRepository` |
| `IBrokerAdmissionModel` | profile/rate/latency/failure → допуск | `ProcessFromClientPublish` |
| `IBrokerMetricsSink` | попытки/доставки/квантили | `BrokerMeasurement` |
| `IExperimentRunner` | warmup → measure → drain → summary | 4–5 harness |
| `IResultWriter` | версионированная схема, сортировка, seal | 4–5 экспортёров |
| `IJobStore` / `IJobScheduler` | персистентность и очередь Web | `JobRegistry` + `SimulationJobRunner` |

### 4.2 Принципы

1. **Один владелец на состояние.** Узел владеет своими очередями; сеть — своими; сессия —
   своими pending. Всё, что читается из другого потока, — либо immutable snapshot, либо
   `Concurrent*`/`Interlocked` с явно описанным инвариантом.
2. **Границы тика — границы изменчивости.** Внутри тика состояние мутабельно; между тиками
   публикуется immutable snapshot. Это убирает гонки «configure vs refresh» и «counter vs UI».
3. **Явные инварианты вместо комментариев.** `PassTillNextTick`, порядок ready/deferred,
   quiescence — либо типы (`DueTick`, `DeferredRing`), либо assertions в debug.
4. **Детерминизм по построению.** Один seeded RNG, никаких `Guid.NewGuid`/`DateTime.Now` в
   модели; сортировка всех экспортируемых коллекций; сид входит в каждый сэмпл.
5. **Ошибка — это ошибка.** Никаких `catch {}`, `Reset()` при сбое и статуса `Completed`
   после исключения. Отдельный `Failure`/`DrainTimeout` статус.
6. **Метрики — доказательство.** Core-метрики отделены от diagnostics; схема и версия
   фиксированы; benchmark проверяет именно продуктовый движок.

---

## 5. Порядок переписывания

### S0. Заморозить поведение (characterization, без изменений кода)

- Golden packet trace: для набора конфигураций писать `(packetId, from, to, enqueueTick,
  deliverTick, payloadHash, type)` в NDJSON; сравнение old/new побайтово (сортировка по id).
  Покрыть seq и par, `strategy 0/1`, QoS 0/1/2, LB/Relay/Federation.
- Metric parity: counts/unique/goodput/p50–p99 — exact; receiver latency/queueDrops — tolerance.
- Determinism: два прогона с одним сидом → одинаковые `summary.json`/`publications.json`
  после сортировки.
- Зафиксировать CLI-коды, имена файлов, HTTP-статусы.
- **Выход:** CI-набор, который падает при любом изменении наблюдаемого поведения.

### S1. Швы ядра (механически, ~низкий риск)

- `TickPipeline` + `TickContext`; `Refresh` делегирует стадиям.
- `TopologyRegistry` + double-buffered `TopologySnapshot`; убрать `OrderBy`/`Values.ToArray()`
  из тика.
- `ISimulationClock`; `CounterClock` — временный shim.
- `IAdmissionPolicy`; исправить owner-thread инвариант `PruneAdmissions`.
- Поведение не меняется; параллельный режим начинает уважать `strategy` (это осознанное
  изменение — либо задокументировать, либо сохранить текущую асимметрию под флагом).
- **Acceptance:** golden trace совпадает для seq; par — совпадает после сортировки и с
  задокументированным допуском на межсетевой порядок.

### S2. Транспорт, ориентированный на данные (средний/высокий риск)

- Заменить `NodeMonitoringPacket`/`NetworkMonitoringPacket`, 4 deferred-списка и packet pool
  на per-entity ring `PacketSlot { packet; dueTick; pathIndex; flags }`.
- Сохранить ровно: `dueTick = tick + 1` при `PassTillNextTick`, decrement раз в refresh,
  ready-then-deferred порядок, TTL при enqueue, silent refusal.
- **Выгода:** минус 2 типа-обёртки, минус pool и его утечки, минус `Path.Skip(1)` и
  делегаты на узел/тик.
- **Acceptance:** golden trace пакет-в-пакет; perf: α и b не хуже.

### S3. MQTT: агрегат сессии и codec (средний риск)

- `ClientSession` → `SessionAggregate` с раздельными `OutboundFlow`/`InboundFlow` по packet id,
  явными переходами FSM и одним lock на операцию.
- `InMemoryRepository.Update` перестаёт пересобирать snapshot; snapshot только на
  create/delete.
- Codec на `Span<byte>`/`IBufferWriter<byte>` с `ArrayPool`; кэш сериализованного envelope
  на (topic, qos); индекс топиков без `Split` на каждый матч.
- Admission/metrics вынести из протокола; `NullSink` при выключенных счётчиках.
- Решить судьбу keep-alive/retained/unsubscribe/resume: реализовать или `NotSupported`.
- **Acceptance:** property-тесты FSM (коллизия id, duplicate PUBLISH после PUBREL, reorder,
  drop, reconnect); QoS2 exactly-once; parity метрик.

### S4. Общий experiment harness (средний риск)

- Один `TopologyBuilder` (LB/Relay/Federation — стратегии, не копии).
- Один `LoadDriver` (credit scheduler + token bucket) и один `DrainPolicy`.
- Один `MetricsCollector` и один `IResultWriter` с версией схемы и сортировкой.
- Один `Options`-тип на движок: immutable record + `Validate()` + единый JSON-policy.
- Один CLI-хост: команды `demo/validate/run/matrix`, общий парсер и коды возврата.
- Benchmark переключить на продуктовые движки; убрать hardcoded `"completed"`.
- **Acceptance:** результаты старых и новых прогонов совпадают; benchmark сравнивает
  одинаковые топологии/тики/QoS.

### S5. Приложения (Web/Designer, средний риск)

- Web: `MqttY.Application` с handlers (`CreateSimulation`, `ValidateTwin`, `ManageJob`),
  контроллеры — 5–10 строк; типизированные `ProblemDetails`; `[Authorize]`/CORS/HTTPS/
  rate limit; версионированные persistence-записи; проверка `OutputDir` при чтении.
- Designer: `DesignDocument` (чистый POCO: сети, узлы, связи, позиции, zoom) + `IDesignLayoutEngine`;
  один общий маппинг с Web; починить сохранение `QueueSize`/`SendTicks` и layout.
- **Acceptance:** golden save/load; API-контрактные тесты; security-тесты на path traversal
  и отсутствие авторизации.

---

## 6. Матрица риск/выгода/усилие

| Этап | Выгода | Риск | Усилие | Блокирует |
|---|---|---|---|---|
| S0 characterization | Делает все следующие этапы безопасными | Низкий | M | — |
| S1 швы ядра | −700 строк God-object, тестируемость, убирает гонку configure/refresh | Низкий | M | S0 |
| S2 транспорт | Убирает pool/deferred/обёртки, снижает α | **Высокий** (латентности) | L | S0, S1 |
| S3 MQTT aggregate | Убирает aliasing/утечки, ускоряет hot path | Средний | L | S0, S1 |
| S4 harness | Убирает дублирование и «зелёные пустые» прогоны | Средний | L | S0, S1 |
| S5 apps | Безопасность, тестируемость, сохранение данных | Средний | L | S4 |

**Рекомендуемый порядок:** S0 → S1 → S4 → S3 → S2 → S5. S4 раньше S3/S2, потому что общий
harness сразу даёт сравнимые метрики и убирает самые дорогие в поддержке копии; S2 — последним
из «ядра», так как это самый рискованный по латентностям этап.

### Статус S0/S1 (2026-11-08)

**S0 выполнен.** Добавлен `PacketTrace` (включается только на время characterization-прогона) и
golden-набор `Fixtures/core-packet-trace-{seq0,seq1,par1}.golden.ndjson`. Тест
`CharacterizationPacketTraceTests` фиксирует send/enqueue/receive-тики для локального и
удалённого пакета, проверяет детерминизм и сравнивает seq/parallel (1 воркер) со снапшотом.
При выключенном трассировщике горячий путь платит только один `Volatile.Read` на Send/Receive.

**S1 выполнен в объёме швов:**

| Шов | Реализация | Что сохранено |
|---|---|---|
| Часы | `ISimulationClock` / `SimulationClock`; `INetworkSimulator.Clock` | тот же тик, те же `ThreadLocal`-дельты шагов, тот же снапшот на refresh |
| Топология | `TopologyRegistry` + `TopologySnapshot` | те же компараторы (`Ordinal` по имени; адрес → тип узла), сортировка только при смене версии |
| Тик | `TickContext` + `TickPipeline` (`PublishClock → RefreshTopology → CompleteTick`) | прежний порядок: topology snapshot → clock/counters → refresh → `Tick()` → real/virtual time |
| Admission | `IAdmissionPolicy` / `TokenWindowAdmission` | reserve → повторная проверка очереди → commit/rollback, тот же rolling byte budget |
| Strategy | явно задокументировано | parallel всегда использует flat two-phase (strategy 0); strategy 1 остаётся sequential-only |

Проверка: **392/392** теста; golden-трассы совпали. Замеры после S1 (4 воркера):
LB `1.61× wall / 1.74× refresh`, Relay `2.28× / 2.38×`, Federation `1.98× / 2.47×` — в пределах
шума относительно baseline. `CounterClock` оставлен как совместимый shim до этапа S3/S4.

### Статус S4a / S3a / S3b (2026-11-08)

**S4a выполнен.** Общий harness-слой: `CreditLoadDriver` (кумулятивный credit-планировщик,
serial/parallel), `ExperimentLoops.Until` (drain с таймаутом) и `HistogramStatistics`
(merge/quantile). LB и Federation используют их; арифметика распределения слотов, `scheduled`,
`attempts`, `rejected` и порядок refresh сохранены. Проверка: 392/392, LB `1.58× wall / 1.74× refresh`.

**S3a выполнен.** `InMemoryRepository.Update` больше не пересобирает snapshot, если хранится тот
же экземпляр (MQTT обновляет именно его). Это убирает O(сессий) копию с каждого протокольного
сообщения. Добавлены `InMemoryRepositoryTests`. Замер LB QoS2 (4 воркера, median 5):
seq wall `3.29 с` (было ~3.82), par `1.74 с` (было ~2.10), refresh par `1.51 с` (было ~1.80).

**S3b выполнен.** PUBACK теперь матчит только `PendingToAcknowledge`: раньше `FirstOrDefault`
по одному `PacketId` мог удалить входящий QoS2-рекорд (`AwaitingPublishRelease`) из той же
сессии. Исправлено на broker и client; добавлены два регрессионных теста на коллизию id.
Проверка: **398/398**.

**S3c выполнен.** Повторный QoS2 PUBLISH с `DUP=1` после PUBCOMP больше не открывает flow
заново: broker отвечает PUBREC и не сохраняет `AwaitingPublishRelease`, поэтому повторный
PUBREL не делает второй fanout. PUBLISH без DUP по-прежнему может переиспользовать packet id.
Добавлен регрессионный тест. Проверка: **399/399**.

**S4b выполнен.** Бенчмарк больше не принимает «зелёный пустой» прогон: `activity=ok` требует
`steps > 0`, а для LB/Fed ещё `attempts > 0 && unique > 0`; иначе `status=invalid`, strict
возвращает 2. `status=completed` только если все сэмплы completed. Исключение одного сэмпла
превращается в `status=error` и не роняет отчёт. Каталог экспорта уникален для каждого сэмпла
(`...-{rep}-{serial}`), а `MQTTY_WORKERS` выставляется на время сэмпла и восстанавливается.
Smoke с экспортом и warmup проходит; gate LB `activity=ok`, `core=match`.

**Следующее по плану:** eviction завершённых QoS2-маркеров / общий result schema, затем S2
(data-oriented транспорт) и S5 (Web/Designer).

### Статус S5a (2026-11-08)

Web hardening без изменения API:

- `JsonFileStore.IsWithinRoot` + проверка persisted `OutputDir` в `GetResults`, `Download` и
  `ExportManifest`; путь результата дополнительно проверяется после `Path.Combine`.
- Глобальный `UseExceptionHandler` возвращает `application/problem+json` вместо
  framework-500 и не раскрывает stack trace.
- `RunSettings=null` даёт 400; `ToStatusDto` null-safe по `RunOptions.Graph.Ticks`.
- Тесты: `FileStoreSafetyTests.IsWithinRoot_*`, `SimulationsApiTests.CreateSimulation_WithoutRunSettings_*`.
  Web **53/53**, Integration **399/399**.

**Следующее по плану:** S2 (data-oriented транспорт) — под golden-трассами S0; затем eviction
QoS2-маркеров и общий result schema.

### Статус S3d (2026-11-08)

`PendingAcknowledgements` больше не растёт без границ:

- у `PendingAcknowledgement` появился `Tick`; broker и client проставляют его при завершении QoS2;
- `PrunePendingAcknowledgements` удаляет `PublishComplete` старше `MQTTY_QOS2_COMPLETED_TTL`
  (по умолчанию 4096 тиков) и ограничивает их число `MQTTY_QOS2_COMPLETED_CAP` (4096) на сессию;
- очистка запускается раз в 64 тика в `MqttBroker.Refresh` и `MqttClient.Refresh`;
- другие типы acknowledgement не затрагиваются; добавлены тесты TTL/capacity.
  Проверка: **402/402**. QoS2 LB: seq `3.08 с`, par `1.65 с` (4 воркера, median 3).

**Следующее по плану:** S2 (data-oriented транспорт) — под golden-трассами S0; затем общий
result schema и Designer (`DesignDocument`).

### Статус S2a (2026-11-08)

Подготовка к S2 без смены модели доставки:

- `Node.SendToNetwork` уважает `MQTTY_NO_PACKET_POOL` — при выключенном пуле пакет больше не
  добавляется в `ConcurrentBag` (раньше bag рос без границ).
- `NodeMonitoringPacket.Reset` очищает `ManualResetEventSlim` в WaitMode; переиспользованный
  пакет больше не наследует старое `Release()`.
- Добавлен `NodeMonitoringPacketTests`. Проверка: **400/400**.

Полный S2 (slot rings вместо обёрток/deferred/pool) остаётся отдельным крупным шагом.

---

## 7. Быстрые исправления независимо от переписывания

Их можно сделать отдельными маленькими коммитами под защитой S0; каждое меняет поведение,
поэтому требует явного решения.

| # | Дефект | Файл | Эффект |
|---|---|---|---|
| 1 | `Stop()` удаляет по `Address`, `Start()` добавляет по `Name` | `Server.cs:99-110`, `Application.cs:143-155` | сервер/приложение не удаляются, флаг врёт |
| 2 | outbound-счётчик берёт inbound `TickFirstValue`, деление без guard | `Counter/NetworkCounters.cs:145-153` | неверная серия throughput |
| 3 | `RefreshWithCounters` делает `Reset()` при ошибке; relay-runner игнорирует `bool` | `NetworkSimulator.cs:910-925`, `MqttRelayExperimentRunner.cs:340-366` | «Completed» при пустых метриках |
| 4 | `InMemoryRepository.Update` пересобирает snapshot | `Internals/InMemoryRepository.cs:34-38` | O(сессий) аллокаций на пакет |
| 5 | packet pool асимметричен при `MQTTY_NO_PACKET_POOL` | `Node.cs:195-212` | утечка bag |
| 6 | `_responseCount` не декрементится при доставке | `Node.cs:276,321,366` | ложный `HasPendingResponse` |
| 7 | `AddClient` оставляет `_clients` при провале `_nodes.TryAdd` | `NetworkSimulator.cs:775-798` | фантомный клиент |
| 8 | `Pump` игнорирует результат `Publish` | `StaticFederator.cs:97-107` | потеря доставки без счётчика |
| 9 | parallel refresh игнорирует `strategy` | `NetworkSimulator.cs:412` | seq/par несравнимы |
| 10 | `Publications()` не сортируется | `FederationDeliveryLedger.cs:148` | недетерминированный sha256 |
| 11 | Benchmark: Relay `"completed"`, нет delivery-метрик, конфликт export/warmup | `Benchmark.Compare/Program.cs:74-75,118-159` | ложный зелёный результат |
| 12 | QoS2 duplicate-after-PUBREL делает повторный fanout | `MqttBroker.cs:304-322,589-590` | нарушение exactly-once |

---

## 8. Как доказать, что функциональность не изменилась

1. **Packet-trace parity.** NDJSON со всеми пакетами; сортировка по `(tick, id)`; diff old/new.
   Это сильнее сравнения summary: ловит изменение латентности на 1 тик.
2. **Metric parity.** Core-метрики — exact; diagnostics — допуск, зафиксированный в тесте.
3. **Determinism.** Два прогона с одним сидом; все экспортируемые коллекции отсортированы;
   `sha256.json` совпадает.
4. **Property-based FSM tests.** Для каждой пары (состояние, пакет) — допустимый переход или
   ошибка; покрыть коллизии id, duplicate, reorder, reconnect, queue drop.
5. **Perf gate.** Текущий `Benchmark.Compare` с warmup/alternating/median; фиксировать α, b,
   refresh seq/par; регрессия >5% — откат.
6. **Security regression.** Тесты на отсутствие auth, path traversal через `OutputDir`,
   произвольную запись job-файла.
7. **Golden files.** `summary.json`, `publications.json`, `topology.graphml` — байтовое
   сравнение после сортировки.

---

## 9. Что не переписывать

- **BSP-модель тика** и двухфазный refresh: это научная модель, event-driven её изменит.
- **Латентности 2/3 тика** и порядок ready/deferred: это контракт, а не баг.
- **Квази-покой как конъюнкция** очередей и plugin-состояния (без `_responseMessages`).
- **Лаг счётчиков на тик** и `RealTime` от первого `Refresh` — иначе поедут все серии.
- **Профили и калибровку** (CDF, интерполяция, сиды) — это научная ценность.
- **Диагностические поля** (`receiver latency`, `queueDrops`, `fill`, `evicted`) — их нельзя
  «оптимизировать» удалением.
- **Packet identity/boxing** до S2: менять `record struct` раньше, чем транспорт, опасно.

---

## 10. Вывод

Переписывать нужно не «всё», а **швы**:

1. сначала заморозить наблюдаемое поведение (S0);
2. выделить часы, snapshot топологии и tick pipeline (S1);
3. объединить experiment harness и метрики (S4);
4. превратить MQTT-сессию в агрегат с явной FSM и убрать аллокации (S3);
5. только затем менять транспорт на data-oriented (S2);
6. приложения (Web/Designer) — тонкими адаптерами поверх готовых use-case (S5).

Такой порядок даёт измеримые улучшения на каждом шаге (меньше дублирования, меньше гонок,
меньше аллокаций), не меняя научные результаты. Главный риск — S2; главная ценность —
S0+S1+S4, потому что именно они превращают набор скриптов в проверяемую систему.
