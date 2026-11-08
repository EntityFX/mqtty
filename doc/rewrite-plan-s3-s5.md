# План выполнения S3–S5

Документ уточняет `doc/rewrite-review.md`: конкретные шаги, файлы, тесты, критерии приёмки и
порядок коммитов. Каждый шаг — отдельный коммит; после каждого шага полный набор тестов и
сравнение golden-трасс/метрик.

---

## S3. MQTT: session aggregate, codec, admission

**Цель:** одно источник истины для состояния сессии, раздельные inbound/outbound packet-id,
явная FSM QoS 0/1/2, меньше аллокаций в hot path, admission/metrics вне протокола.

### S3f. SessionAggregate и раздельные flow-namespaces — ВЫПОЛНЕНО

**Факт:** `ClientSession` хранит outbound/inbound в отдельных словарях с одним
session lock; протокол использует `TryStartInboundPublish`, `TryCompleteInboundPublish`,
`TryAcknowledgeOutbound`, `TryAdvanceOutboundQos2`, `TryCompleteOutboundQos2`,
`UpsertSubscription`. `PendingMessages`/`PendingAcknowledgements` оставлены
read-only compatibility-снимками для диагностики. Пять новых FSM-тестов;
Integration **412/412**, core-метрики LB/Relay совпадают.

**Проблема.** `ClientSession` — три публичных `List<>`; inbound и outbound записи лежат в одном
`PendingMessages`, ключ — только `PacketId`. PUBACK уже отфильтрован по статусу (S3b), но
структурной гарантии нет; FSM размазана по `MqttBroker`/`MqttClient`.

**Шаги.**
1. Ввести `SessionState` (внутри `ClientSession` или рядом):
   - `Dictionary<ushort, OutboundFlow>` — broker→peer: `PendingToAcknowledge`,
     `AwaitingPublishReceived`, `AwaitingPublishComplete`;
   - `Dictionary<ushort, InboundFlow>` — peer→broker: `AwaitingPublishRelease`;
   - `Dictionary<string, ClientSubscription>` — подписки;
   - bounded `CompletedPublishIds` (уже есть TTL/cap из S3d) — для DUP-подавления;
   - один lock на сессию (или отдельные lock на outbound/inbound).
2. Перевести чтение/запись на методы FSM:
   `OnPublish`, `OnPubAck`, `OnPubRec`, `OnPubRel`, `OnPubComp`, `OnSubscribe`.
   Каждый переход возвращает команду (`SendPubAck`, `SendPubRec`, `Fanout`, `Drop`, `None`).
3. Оставить `ClientSession` compatibility-фасадом для `MqttPendingSnapshotBuilder` и
   диагностики; `GetPendingMessages()` собирает union для отчёта, но протокол его не использует.
4. Удалить дублирующиеся find-mutate-remove блоки в `MqttBroker`/`MqttClient`.

**Файлы:** `Internals/ClientSession.cs`, `Internals/PendingMessage.cs`,
`Internals/ClientSubscription.cs`, `Internals/StorageExtensions.cs`, `MqttBroker.cs`,
`MqttClient.cs`, `Diagnostics/MqttPendingSnapshotBuilder.cs`.

**Тесты.**
- Property/FSM: коллизия packet-id inbound/outbound; duplicate PUBLISH до/после PUBREL;
  duplicate PUBREL; reorder PUBACK/PUBREC; drop; reconnect; clean/persistent session.
- Регрессия: `BrokerQos2Tests`, `MqttProtocolTests`, `MqttStagedTests`, `MqttLongConfTests`.
- Golden: QoS2 LB/Fed метрики и receiver-латентность в допуске.

**Приёмка:** 100% текущих тестов + новые FSM; нет `FirstOrDefault` по `PacketId` без
direction/status; `IsQuiescent` не меняется.

**Оценка:** 5 ч/д.

### S3g. Codec без `List<byte>` и LINQ по байтам — ВЫПОЛНЕНО

**Факт:** все formatters пишут в один `byte[]` через `Span<byte>` и
`BinaryPrimitives`; `MqttEncoder` больше не строит `List<byte>`, `ByteExtensions`
не использует `Skip/Take/FirstOrDefault`, `ConnectFormatter` держит статический
`Regex`. `MqttTopicEvaluator` кэширует разобранные topic/filter (bounded 8192,
счётчик без `ConcurrentDictionary.Count`), поэтому fanout не делает `Split('/')`
на каждый publish. Integration **420/420**, core LB/Relay совпадает.

Замеры на 12C/24T, workers=12, repeat 3:

| Конфиг | до S3g | после S3g |
|---|---|---|
| LB reference wall/refresh | 2.01× / 2.14× | **2.12× / 2.31×** |
| Relay reference wall/refresh | 3.23× / 3.47× | **3.02× / 3.34×** |
| LB L wall/refresh | 3.94× / 4.52× | **3.68× / 4.27×** |
| Relay L wall/refresh | 2.89× / 2.99× | **3.12× / 3.25×** |
| Relay L allocated | 75.8 ГБ | **29.0 ГБ (−62%)** |
| Federation seq, 256 pub / 2k rps | 57.1 с, 44.5 ГБ | **45.8 с, 13.4 ГБ (−70%)** |

**Проблема.** `PublishFormatter`/`MqttEncoder`/`ByteExtensions` делают `List<byte>`, `Skip/Take`,
копируют payload на каждого подписчика; `MqttTopicEvaluator` делает `Split('/')` на каждый матч;
`ConnectFormatter` компилирует `Regex` на каждый вызов.

**Шаги.**
1. Переписать `PublishFormatter.Write`/`Read` на `Span<byte>`/`IBufferWriter<byte>` +
   `ArrayPool<byte>`; один буфер на пакет, без промежуточных `List`.
2. `ByteExtensions`: заменить `Skip/Take/FirstOrDefault` на индексы и `Span`.
3. `MqttTopicEvaluator`: кэш разобранного фильтра/топика (по строке), матчинг по сегментам без
   `Split` на каждый publish.
4. `ConnectFormatter`: статический `Regex` (или ручная проверка client id).
5. Кэш сериализованного envelope в fanout: QoS0 — один `byte[]` на публикацию (уже есть),
   QoS1/2 — префикс без packet id, id подставляется в буфер.
6. Оставить debug-dumper на старом formatter path, если нужен для тестов.

**Файлы:** `Internals/Formatters/*`, `MqttEncoder.cs`, `ByteExtensions.cs`,
`MqttTopicEvaluator.cs`, `MqttNativePacketManager.cs`.

**Тесты.** `MqttProtocolTests`, byte-level round-trip, property round-trip
`Write(Read(x)) == x`; сравнение байтов старый/новый codec на корпусе пакетов; метрики QoS2.

**Приёмка:** аллокации на fanout/receive снижаются (замер `GC.GetTotalAllocatedBytes`);
поведение и байты пакетов не меняются.

**Оценка:** 4 ч/д.

### S3h. Admission и metrics вне протокола — ВЫПОЛНЕНО

**Факт:** добавлены `IBrokerAdmissionModel`, `NoopBrokerAdmissionModel`,
`ProfileBrokerAdmissionModel` (`Admission/`) и `IBrokerMetricsSink`
(`Counter/`). `MqttBroker` больше не содержит профиль/sampler/rate-limiter и
latency-формулы — он получает `BrokerAdmission` с `Admit`, `DelayTicks`,
`RejectReason`, `ConditionalDeliveryLossRate`. `BrokerMeasurement` ключуется
value-tuple `PublicationKey`, `LatencyHistogram` стал dense tick-массивом с
overflow-картой (запись O(1), snapshot без копирования `SortedDictionary` на
каждый тик). Scientific metrics сознательно остаются включёнными при
`EnableCounters=false`: их использует benchmark/экспорт; флаг отключает
legacy-счётчики. Integration **424/424**, negative admission paths покрыты
`BrokerAdmissionTests`.

Замеры 12C/24T, workers=12, repeat 3: LB reference **1.96×/2.06×**, Relay
reference **2.90×/3.23×**, LB L **3.82×/4.47×**, Relay L **3.11×/3.30×**;
core/diagnostics match.

**Проблема.** `MqttBroker.ProcessFromClientPublish` совмещает protocol, profile, rate limiter,
failure sampler, latency model, `BrokerMeasurement`; глобальный `lock` и строковые ключи в
hot path; metrics считаются даже при выключенных счётчиках.

**Шаги.**
1. `IBrokerAdmissionModel`:
   `Admission TryAdmit(PublishContext)` → `{ Admit, DelayTicks, RejectReason }`.
   Реализации: `NoopAdmission` (профиль отсутствует), `ProfileAdmission` (текущая логика).
2. `IBrokerMetricsSink`: `BeginAttempt`, `ExpectDelivery`, `CompletePublisher`,
   `DeliveryDropped`; `NullSink` при `EnableCounters=false`. `BrokerMeasurement` — реализация.
3. Ключи измерений — value-tuple `(string Publisher, byte Qos, ushort PacketId)` или struct,
   без интерполяции строк на каждое сообщение.
4. `LatencyHistogram`: ring/массив по тикам вместо `SortedDictionary`; nearest-rank за один
   проход; snapshot без deep copy.
5. `MqttBroker` вызывает admission/metrics, но не знает их внутренностей.

**Файлы:** `MqttBroker.cs`, `Counter/BrokerMeasurement.cs`, `Counter/MqttCounters.cs`,
`Counter/LatencyHistogram.cs`, новые `Admission/`, `Metrics/`.

**Тесты.** `BrokerMeasurementTests`, `BrokerMetricsValidationTests`, `MqttLongConfTests`;
parity метрик при counters on/off; negative admission/reject paths.

**Приёмка:** counters-off прогон не платит за профиль/SHA/лок; метрики совпадают с текущими.

**Оценка:** 4 ч/д.

### S3i. Полнота протокола (явный scope) — SCOPE ЗАФИКСИРОВАН

**Факт:** неподдерживаемые возможности объявлены явно, чтобы модель не теряла
сообщения молча. `MqttClient.Unsubscribe/BeginUnsubscribe/CompleteUnsubscribe`
бросают `NotSupportedException` с ссылкой на этот раздел; брокер делает то же при
получении UNSUBSCRIBE. Integration **425/425**.

| Возможность | Статус | Что делает модель |
|---|---|---|
| Keep-alive / PINGREQ / PINGRESP | **не поддерживается** | таймеров нет; входящий PINGREQ игнорируется, клиент не отправляет PING |
| UNSUBSCRIBE | **не поддерживается** | `NotSupportedException` на клиенте и брокере |
| DISCONNECT-пакет | **не используется** | `Client.Disconnect()` локально отцепляет клиента; broker удаляет clean-сессию по событию disconnect |
| Retained store/replay | **не поддерживается** | флаг Retain передаётся подписчикам, но сообщение не хранится и не воспроизводится при SUBSCRIBE |
| Session resume/DUP | **частично** | persistent-сессия сохраняет подписки и pending-записи, но после reconnect они не переотправляются с DUP |
| Pending без доставки | **виден** | `IsQuiescent` учитывает pending-сообщения/acknowledgement'ы, поэтому потеря не маскируется |

Реализация этих возможностей вынесена за пределы текущего scope; интерфейс
`IMqttClient` по-прежнему содержит методы, но они не притворяются работающими.

**Проблема.** Keep-alive/PING, DISCONNECT-очистка, UNSUBSCRIBE, retained store/replay,
session resume/DUP либо отсутствуют, либо заглушены. Интерфейс обещает больше, чем модель.

**Шаги.**
1. Зафиксировать scope: либо реализовать, либо `NotSupportedException` + документация.
2. Если реализуем:
   - retained: `Dictionary<topic, PendingMessage>` + replay при SUBSCRIBE;
   - keep-alive: таймер на клиенте/брокере, PINGREQ/PINGRESP;
   - DISCONNECT: удаление clean-сессии, detached persistent;
   - resume: повтор `PendingToSend`/`Awaiting*` с DUP.
3. Явно исключить потерянные pending из `IsQuiescent` (retry или fail).

**Файлы:** `MqttBroker.cs`, `MqttClient.cs`, `ClientSession.cs`, `Packets/*`.

**Тесты:** retained replay, keep-alive timeout, disconnect/clean, reconnect/DUP.

**Оценка:** 4 ч/д (в зависимости от scope).

---

## S4. Общий experiment harness и result schema

**Цель:** один набор примитивов для LB/Relay/Federation, версионированные результаты,
детерминированный экспорт, единый CLI, benchmark на продуктовых движках.

### S4d. Result schema и writer — ВЫПОЛНЕНО

**Факт:** добавлен единый `EntityFX.MqttY.Results`:
`ResultSchema.Version = 1`, `ExperimentManifestV1`, `ExperimentArtifactV1`,
`IResultWriter`/`JsonResultWriter`. Writer делает атомарную замену через
`.tmp`, потоковую JSON-сериализацию, сортированный `sha256.json` (потоковый
хеш, без чтения файла в массив) и умеет писать текст/байты. LB, Federation и
Relay используют его; имена файлов не изменились. Relay теперь тоже пишет
`sha256.json`, а его manifest получил `schemaVersion`/`experiment`.
Integration **427/427**; golden-тест `JsonResultWriterTests` проверяет, что две
одинаковые записи дают побайтно одинаковые `summary.json` и `sha256.json`, а
неудачная запись не заменяет существующий артефакт.

1. `ExperimentResultV1` (schemaVersion, provenance, phases, metrics core/diagnostics,
   artifacts, hashes). `ExperimentManifestV1` отдельно.
2. Один `IResultWriter` с сортировкой всех коллекций, компактным/indented режимом, хешем при
   записи; адаптеры LB/Fed/Relay сохраняют текущие имена файлов.
3. `sha256.json` считается по отсортированным путям и содержимому.
4. Golden-тесты: два одинаковых прогона → идентичные `summary.json`, `publications.json`,
   `sha256.json`.

**Файлы:** `Engines/*/Results/*`, `MqttRelayExperimentRunner.Export`, `Benchmark.Compare`.
**Оценка:** 4 ч/д.

### S4e. Options/JSON policy — ВЫПОЛНЕНО

**Факт:** добавлен `EntityFX.MqttY.RuntimeOptions` — снимок `MQTTY_*`
переменных на момент создания симуляции. `NetworkSimulator` и `TickWorkerPool`
больше не держат process-wide `static readonly` для workers/barrier/spin/chunks/
stats/counters/active-only/nonatomic; benchmark может менять окружение между
сэмплами. LB/Fed-раннеры читают `MQTTY_PARALLEL_PUBLISH`/`MQTTY_ISOLATE_NETWORKS`
из снимка, `CreditLoadDriver` — из того же источника. Добавлен единый
`JsonPolicies.Result` (camelCase, indented, enum names only); его используют
LB/Fed/Relay. Для входов LB/Fed сохранена строгая проверка неизвестных полей.
Integration **428/428**.

1. Immutable record options на движок + `Validate()`; один `JsonSerializerOptions` policy.
2. `RuntimeOptions` для env (`MQTTY_WORKERS`, barrier, chunks, counters) с defaults/override;
   убрать `static readonly` там, где нужен переключатель в процессе.
3. Схемы/версии для inputs; unknown fields — ошибка или warning (по типу документа).

**Оценка:** 3 ч/д.

### S4f. Единый CLI и benchmark на продуктовых движках — ВЫПОЛНЕНО

**Факт:** добавлен единый host `EntityFX.MqttY.Experiment.Cli`
(`lb|fed|relay` → `demo/validate/run/matrix` или `experiment/calibrate`,
коды 0/1/2, `--help`). Reference benchmark больше не использует legacy `MqttRelayApp`:
Relay-плечо идёт через `FederationRunner(Mode=Relay)` — тот же путь, что у
продуктового `RelayEngine`. Activity-гейт теперь одинаков для всех движков:
`steps > 0 && attempts > 0 && unique > 0`; у Relay появились реальные
attempts/unique/core-метрики. `--no-export` по-прежнему не меняет научные
метрики, seq/par используют одну топологию и QoS. Проверка: Relay
`--repeat 1 --measure 1 --fed-rps 2000` дал `completed`, `activity=ok`,
`core=match`, `wall 3.72× / refresh 4.87×`.

1. Один CLI-host: `demo/validate/run/matrix`, общий парсер, коды 0/1/2, `--help`.
2. Benchmark: `RelayEngine`/`FederationEngine`/`LoadBalancingEngine` вместо legacy
   `MqttRelayApp`; убрать hardcoded status; проверять delivery/activity.
3. Одинаковые topology/tick/QoS для seq/par; `--no-export` не влияет на науку.

**Оценка:** 4 ч/д.

### S4g. MetricsCollector — ВЫПОЛНЕНО

**Факт:** добавлен общий `EntityFX.MqttY.Engines.Execution.MetricsCollector`:
atomic counters для scheduled/attempts/rejected/unique/errors и
publication-latency samples. Snapshot возвращает типизированные
`CoreMetrics`/`DiagnosticMetrics`; LB/Fed-раннеры используют коллектор для
счётчиков, benchmark сериализует core/diagnostics через него. Экспорт остаётся
раздельным (`summary.json`, `histograms.json`), дублирования summary нет.
Integration **431/431**, добавлены тесты goodput/nearest-rank/concurrency.

Замеры 12C/24T, workers=12, repeat 3: LB reference **2.01×/2.08×**; Relay
(product FederationRunner, 16 брокеров, 64 публикатора, 2k rps) —
**4.08×/4.91×**, `core=match`, `diagnostics=match`.

1. Один collector: credit scheduler → attempts/rejected/unique/goodput/quantiles.
2. Core vs diagnostics — явные типы; экспорт не дублирует summary.
3. Замеры: benchmark gate, `α`/`b`, отсутствие регрессии >5%.

**Оценка:** 2 ч/д.

---

## S5. Приложения: Web, persistence, Designer

### S5d. Web application layer — ВЫПОЛНЕНО

**Факт:** добавлен слой `EntityFX.MqttY.Web.App.Application`:
`ApplicationResult<T>` (Ok/Accepted/NoContent/BadRequest/NotFound/Conflict),
`SimulationCreationHandler`, `SimulationJobHandler`, `SaveTopologyHandler`,
`TwinValidationHandler`, `SimulationDtoMapper`. Мутирующие endpoints
`SimulationsController`, `TopologiesController`, `TwinValidationsController`
стали тонкими адаптерами; бизнес-логика больше не живёт в контроллерах.
Глобальный `ProblemDetails`-обработчик уже был в `Program.cs`. Web-тесты
локально не запускаются из-за внешней ошибки NuGet-сертификата (`NU3018`);
Integration **431/431**, Web.App собирается с 0 ошибок.

1. `MqttY.Application`: `CreateSimulationHandler`, `ValidateTwinHandler`,
   `ManageJobHandler`, `SaveTopologyHandler`; typed results (`ValidationError`, `NotFound`,
   `Conflict`, `Accepted(id)`).
2. Контроллеры — тонкие адаптеры; `ProblemDetails`; нет бизнес-логики в controller.
3. Тесты handler'ов без HTTP + текущие API-тесты.

**Оценка:** 5 ч/д.

### S5e. Versioned persistence — ВЫПОЛНЕНО

**Факт:** `SchemaVersion` добавлен в `SavedTopologyDto`, `SimulationJob`,
`MonitoringSnapshotDto`; `PersistenceMigrations.CurrentVersion = 2` повышает V1
без `schemaVersion` при чтении. `JsonFileStore` реализует `IJsonDocumentStore`:
атомарная запись через temp+rename, `Flush(flushToDisk: true)`, per-path
`SemaphoreSlim`, `TryReadAsync` для corrupt-файлов, `Prune` с retention
(мониторинг — 200 последних снимков). `JobRegistry`/`MonitoringService`/
`PresetCatalogService` переведены на `TryReadAsync`, поэтому битый файл не
валит startup. Тесты добавлены в `EntityFX.Tests.Web`
(`VersionedPersistenceTests`: V1→V2, corrupt, concurrent writes, Prune);
локально Web-набор не запускается из-за внешней ошибки NuGet-сертификата.

1. `SavedTopologyRecordV1`, `JobRecordV1`, `MonitoringSnapshotRecordV1` + миграции.
2. `IJsonDocumentStore`: атомарная запись, fsync, per-path lock, retention (workers/logs/
   progress).
3. Проверка `OutputDir`/`RunOptions` при загрузке; corrupt-запись не валит startup.
4. Тесты golden V1→V2, corrupt-файлы, concurrent writes.

**Оценка:** 4 ч/д.

### S5f. Security — ВЫПОЛНЕНО

**Факт:** добавлен опциональный `SecurityOptions` (`MqttYWeb:Security`):
API key (`X-Api-Key` или `Authorization: Bearer`), fixed-window rate limit на
мутирующие API (60/мин по умолчанию), CORS allow-list, HTTPS/HSTS switches,
лимит загрузки (`MaxImportBytes`, Kestrel). Пустой API key сохраняет локальный
open-режим; при заданном ключе действует fallback authorization policy.
Rate limit возвращает `429 application/problem+json`. Пути данных по-прежнему
проверяются `JsonFileStore.Resolve/IsWithinRoot` (path traversal, symlink).
Тесты добавлены в `EntityFX.Tests.Web` (`SecurityTests`: traversal, rate limit);
локально Web-набор не запускается из-за внешней ошибки NuGet-сертификата.

1. AuthN/AuthZ (API key или OIDC) с опциональным включением; `[Authorize]` на API.
2. CORS policy, HTTPS/HSTS, rate limiting на тяжёлые endpoints, лимиты загрузок.
3. Security-тесты: path traversal через persisted `OutputDir`, произвольная запись job-файла,
   oversized import.

**Оценка:** 4 ч/д.

### S5g. Designer `DesignDocument` — ВЫПОЛНЕНО

**Факт:** добавлен UI-независимый слой `EntityFX.MqttY.Design`:
POCO `DesignDocument`/`DesignNetwork`/`DesignNode`/`DesignLink`/`DesignNetworkType`,
двусторонний маппинг `NetworkGraphOption ↔ DesignDocument`, JSON save/load,
`IDesignLayoutEngine` + детерминированный `GridDesignLayoutEngine` и чистый
`GraphMlDesignReader` (GraphML → DesignDocument без Avalonia/ReactiveUI).

**Миграция Designer:** `GraphMLImporterService` больше не содержит собственный
парсер — он делегирует в `GraphMlDesignReader`. `NetworkDesignModel` стал тонким
адаптером: `ToDesignDocument`/`FromDesignDocument`, а `ToNetworkGraphOption`/
`FromNetworkGraphOption` целиком используют общий слой. Заодно сохранены
`Broker`, `Additional` и `SendTicks` (раньше терялись при load/save).
`NetworkEditorViewModel` работает поверх этого адаптера, бизнес-логика в нём
не дублируется. `DesignNode` получил `SendTicks`, GraphML-ридер — прежние
default-спецификации (`mqtt-broker`/`mqtt-client`/`mqtt-relay`).

Проверка: Integration **436/436** (round-trip options/Broker/SendTicks, layout,
JSON, GraphML defaults); Web **62/62**.
Локальный restore в этом окружении требует
`DOTNET_NUGET_SIGNATURE_VERIFICATION=false` (в системе нет цепочки доверия
для подписи пакетов NuGet); это не изменение кода.

1. Чистый POCO `DesignDocument` (сети/узлы/связи/позиции/zoom/ticks), без Avalonia/ReactiveUI.
2. `IDesignLayoutEngine` + `GraphMLReader → ImportDto → validator → DesignDocument`.
3. Общий маппинг с Web; `NetworkEditorViewModel` — команды, без бизнес-логики в code-behind.
4. Тесты save/load, layout, GraphML import без UI.

**Оценка:** 5 ч/д.

---

## Порядок и критерии

1. **S3f** → S3g → S3h → S3i.
2. **S4d** → S4e → S4f → S4g (можно параллельно с S3 после S3f).
3. **S5d** → S5e → S5f → S5g.

**После каждого шага:**
- build 0 ошибок;
- Integration 436/436; Web 62/62;
- golden-трассы S0 совпадают;
- benchmark: `activity=ok`, `core=match`, регрессия wall/refresh ≤5%;
- детерминизм: два прогона с одним seed дают одинаковые отсортированные экспорты.

**Итог:** S3–S5 закрыты (S5g — последний шаг). Остаток по этому плану — 0;
дальнейшие работы — performance plan (`M7`, `M8`, горячий путь Federation).
