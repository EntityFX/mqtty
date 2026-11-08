# План выполнения S3–S5

Документ уточняет `doc/rewrite-review.md`: конкретные шаги, файлы, тесты, критерии приёмки и
порядок коммитов. Каждый шаг — отдельный коммит; после каждого шага полный набор тестов и
сравнение golden-трасс/метрик.

---

## S3. MQTT: session aggregate, codec, admission

**Цель:** одно источник истины для состояния сессии, раздельные inbound/outbound packet-id,
явная FSM QoS 0/1/2, меньше аллокаций в hot path, admission/metrics вне протокола.

### S3f. SessionAggregate и раздельные flow-namespaces

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

### S3g. Codec без `List<byte>` и LINQ по байтам

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

### S3h. Admission и metrics вне протокола

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

### S3i. Полнота протокола (явный scope)

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

### S4d. Result schema и writer

1. `ExperimentResultV1` (schemaVersion, provenance, phases, metrics core/diagnostics,
   artifacts, hashes). `ExperimentManifestV1` отдельно.
2. Один `IResultWriter` с сортировкой всех коллекций, компактным/indented режимом, хешем при
   записи; адаптеры LB/Fed/Relay сохраняют текущие имена файлов.
3. `sha256.json` считается по отсортированным путям и содержимому.
4. Golden-тесты: два одинаковых прогона → идентичные `summary.json`, `publications.json`,
   `sha256.json`.

**Файлы:** `Engines/*/Results/*`, `MqttRelayExperimentRunner.Export`, `Benchmark.Compare`.
**Оценка:** 4 ч/д.

### S4e. Options/JSON policy

1. Immutable record options на движок + `Validate()`; один `JsonSerializerOptions` policy.
2. `RuntimeOptions` для env (`MQTTY_WORKERS`, barrier, chunks, counters) с defaults/override;
   убрать `static readonly` там, где нужен переключатель в процессе.
3. Схемы/версии для inputs; unknown fields — ошибка или warning (по типу документа).

**Оценка:** 3 ч/д.

### S4f. Единый CLI и benchmark на продуктовых движках

1. Один CLI-host: `demo/validate/run/matrix`, общий парсер, коды 0/1/2, `--help`.
2. Benchmark: `RelayEngine`/`FederationEngine`/`LoadBalancingEngine` вместо legacy
   `MqttRelayApp`; убрать hardcoded status; проверять delivery/activity.
3. Одинаковые topology/tick/QoS для seq/par; `--no-export` не влияет на науку.

**Оценка:** 4 ч/д.

### S4g. MetricsCollector

1. Один collector: credit scheduler → attempts/rejected/unique/goodput/quantiles.
2. Core vs diagnostics — явные типы; экспорт не дублирует summary.
3. Замеры: benchmark gate, `α`/`b`, отсутствие регрессии >5%.

**Оценка:** 2 ч/д.

---

## S5. Приложения: Web, persistence, Designer

### S5d. Web application layer

1. `MqttY.Application`: `CreateSimulationHandler`, `ValidateTwinHandler`,
   `ManageJobHandler`, `SaveTopologyHandler`; typed results (`ValidationError`, `NotFound`,
   `Conflict`, `Accepted(id)`).
2. Контроллеры — тонкие адаптеры; `ProblemDetails`; нет бизнес-логики в controller.
3. Тесты handler'ов без HTTP + текущие API-тесты.

**Оценка:** 5 ч/д.

### S5e. Versioned persistence

1. `SavedTopologyRecordV1`, `JobRecordV1`, `MonitoringSnapshotRecordV1` + миграции.
2. `IJsonDocumentStore`: атомарная запись, fsync, per-path lock, retention (workers/logs/
   progress).
3. Проверка `OutputDir`/`RunOptions` при загрузке; corrupt-запись не валит startup.
4. Тесты golden V1→V2, corrupt-файлы, concurrent writes.

**Оценка:** 4 ч/д.

### S5f. Security

1. AuthN/AuthZ (API key или OIDC) с опциональным включением; `[Authorize]` на API.
2. CORS policy, HTTPS/HSTS, rate limiting на тяжёлые endpoints, лимиты загрузок.
3. Security-тесты: path traversal через persisted `OutputDir`, произвольная запись job-файла,
   oversized import.

**Оценка:** 4 ч/д.

### S5g. Designer `DesignDocument`

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
- Integration 407/407 + новые тесты; Web 56/56;
- golden-трассы S0 совпадают;
- benchmark: `activity=ok`, `core=match`, регрессия wall/refresh ≤5%;
- детерминизм: два прогона с одним seed дают одинаковые отсортированные экспорты.

**Итоговая оценка остатка:** ~46–50 ч/д (S3 ~17, S4 ~13, S5 ~18), с буфером ~55–60 ч/д.
