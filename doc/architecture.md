# Архитектура MqttY

Документ описывает текущее устройство проекта и итог переписывания S0–S5.
Числа производительности и методика замеров — в [performance.md](performance.md).
Правила работы агента и команды — в [../AGENTS.md](../AGENTS.md).

## 1. Назначение и границы

MqttY — дискретно-событийный симулятор MQTT-сетей в модельном времени. Он не
запускает настоящие брокеры: клиенты, брокеры, сетевые задержки, очереди и
протокольные обмены воспроизводятся внутри одного процесса. Профили реальных
брокеров (MqttBenchmark) задают задержки, пропускную способность и потери.

Три продуктовых движка:

| Движок | Что моделирует | Точка входа |
|---|---|---|
| **LoadBalancing** | назначение клиентов брокерам: DirectBalanced, RoundRobin, LeastConnections | `EntityFX.MqttY.LoadBalancing.App` |
| **Relay** | профили брокеров, BrokerFidelity, цепочки ретрансляции | `EntityFX.MqttY.MqttRelay.App` |
| **Federation** | центральный брокер, полносвязный relay, статическая федерация с подавлением повторов | `EntityFX.MqttY.Federation.App` |

Общий host экспериментов — `EntityFX.MqttY.Experiment.Cli`; reference-бенчмарк
seq/par — `EntityFX.MqttY.Benchmark.Compare`.

## 2. Проекты

```
src/EntityFX.MqttSimulator/
├── EntityFX.MqttSimulator.Contracts/   # сеть, счётчики, опции, DTO
├── EntityFX.MqttY.Contracts.Mqtt/      # MQTT-пакеты и профили
├── EntityFX.MqttY.Contracts.Scenarios/ # сценарии
├── EntityFX.MqttSimulator/             # ядро: время, топология, транспорт, узлы
├── EntityFX.MqttY.Plugin.Mqtt/         # MQTT: сессии, кодек, admission, метрики
├── EntityFX.MqttY.Plugin.Scenarios/    # исполнение сценариев
├── EntityFX.MqttY.Engines/             # LB / Relay / Federation, общий harness
├── EntityFX.MqttY.Experiment.Cli/      # единый CLI экспериментов
├── EntityFX.MqttY.Benchmark.Compare/   # seq vs par reference-бенчмарк
├── EntityFX.MqttY.LoadBalancing.App/   # CLI LB
├── EntityFX.MqttY.MqttRelay.App/       # CLI Relay
├── EntityFX.MqttY.Federation.App/      # CLI Federation
├── EntityFX.MqttY.Web.App/             # REST API, SignalR, persistence
├── EntityFX.MqttY.Designer/            # Avalonia-редактор
└── EntityFX.MqttSimulator/Design/      # namespace EntityFX.MqttY.Design: DesignDocument, GraphML, layout
```

`EntityFX.MqttY.Design` физически лежит в core-проекте
(`EntityFX.MqttSimulator/Design/`), чтобы Designer и Web могли использовать
общий слой без Avalonia/ReactiveUI.

## 3. Тик и инварианты

Один `Refresh` — одна граница изменчивости:

```
PublishClock (tick, steps, CounterClock)
  → RefreshTopology
      phase 1: все сети
      fence
      phase 2: все узлы
      fence
  → CompleteTick (virtual/real time, counters)
```

Инварианты:

- **Порядок фаз неизменен.** Сеть не может обработать пакет, отправленный узлом
  в том же тике; узел не может обработать доставку, пока сеть не завершила тик.
- **Границы тика — границы видимости.** Внутри тика состояние мутабельно; между
  тиками публикуются неизменяемые снапшоты (`TopologySnapshot`, `CounterClock`).
- **Один владелец на состояние.** Узел владеет своими очередями, сеть — ingress,
  сессия — pending-списками. Всё, что читается из другого воркера, — snapshot,
  `Concurrent*` или `Interlocked`.
- **Детерминизм.** Один seeded RNG, сортировка экспортируемых коллекций, сид
  входит в каждый сэмпл. `DateTime.Now`/`Guid.NewGuid` в модели запрещены.
- **Ошибка — это ошибка.** Исключение в refresh не маскируется; результат
  получает статус `failed`, а не `completed`.

Параллельный режим всегда использует плоский двухфазный refresh (strategy 0).
`strategy=1` («сеть + её узлы на одном воркере») оставлен только для sequential.

## 4. Ядро

| Компонент | Ответственность | Файлы |
|---|---|---|
| `NetworkSimulator` | оркестрация refresh, реестры, packet id, real-time драйвер | `Network/NetworkSimulator.cs` |
| `SimulationClock` / `TickPipeline` / `TickContext` | модельное время, стадии тика, ThreadLocal-дельты шагов | `Network/SimulationClock.cs`, `TickPipeline.cs` |
| `TopologyRegistry` / `TopologySnapshot` | реестры и неизменяемый снимок на тик; сортировка только при изменении версии | `Network/TopologyRegistry.cs` |
| `TickWorkerPool` | постоянные воркеры, двухфазный fence, динамический захват работы | `Network/TickWorkerPool.cs` |
| `Node` / `Network` | очереди ready/deferred, quiescence, ingress, admission | `Network/Node.cs`, `Network/Network.cs` |
| `NodePacketSlot` / `NetworkPacketSlot` | data-oriented транспортные слоты вместо wrapper-классов | `Network/*PacketSlot.cs` |
| `TokenWindowAdmission` | rolling byte budget: reserve → commit/rollback | `Network/TokenWindowAdmission.cs` |
| `CounterClock` / `CounterGroup` | один publish часов на тик, без обхода дерева счётчиков | `Counter/`, `Contracts/Counters/` |
| `PacketTrace` | S0 characterization, NDJSON send/receive, env-gated | `Network/PacketTrace.cs` |

Особенности транспорта:

- ready-очередь обрабатывается по снимку длины; не готовые пакеты уходят в
  `deferred`-списки и не пере-enqueue-ятся каждый тик;
- путь пакета хранится массивом сетей + `NextHop`, без копирования на каждом hop;
- параллельная фаза 1 стартует с самых плотных сетей (общая client-LAN первой),
  иначе она становится хвостом тика;
- admission не сериализует отправителей на мониторе: один `Interlocked` claim.

## 5. MQTT-плагин

### 5.1 Сессии и QoS

`ClientSession` хранит **раздельные** inbound/outbound namespace:

- `Dictionary<ushort, OutboundFlow>` — `PendingToAcknowledge`,
  `AwaitingPublishReceived`, `AwaitingPublishComplete`;
- `Dictionary<ushort, InboundFlow>` — `AwaitingPublishRelease`;
- `Dictionary<string, ClientSubscription>`;
- bounded `CompletedPublishIds` для DUP-подавления.

Переходы FSM: `TryStartInboundPublish`, `TryCompleteInboundPublish`,
`TryAcknowledgeOutbound`, `TryAdvanceOutboundQos2`, `TryCompleteOutboundQos2`,
`UpsertSubscription`. `PendingMessages`/`PendingAcknowledgements` оставлены
read-only снимками для диагностики; протокол их не использует. PUBACK матчит
только `PendingToAcknowledge`, поэтому не может удалить inbound QoS2-запись с
тем же packet id.

### 5.2 Кодек и fanout

- `PublishFormatter`/`MqttEncoder` пишут в один `byte[]` через `Span<byte>` и
  `BinaryPrimitives`; промежуточных `List<byte>` и LINQ по байтам нет.
- `MqttTopicEvaluator` кэширует разобранные topic/filter (bounded 8192);
  `Split('/')` не выполняется на каждый publish.
- `ConnectFormatter` использует статический `Regex`.
- QoS0 fanout сериализует envelope один раз на публикацию и раздаёт один
  read-only массив; QoS1/2 подставляют packet id.
- Снапшот подписок брокера предсортирован и инвалидируется только при
  subscribe/disconnect/clean-session.

### 5.3 Admission и метрики

Протокол больше не содержит профиль/rate-limiter/латентность:

- `IBrokerAdmissionModel` (`Noop`, `Profile`) возвращает `Admit`, `DelayTicks`,
  `RejectReason`, `ConditionalDeliveryLossRate`;
- `IBrokerMetricsSink` (`BrokerMeasurement`) считает попытки, допуски, доставки,
  потери и latency-гистограмму;
- `LatencyHistogram` — dense tick-массив с overflow-картой, запись O(1),
  снапшот без копирования `SortedDictionary` на каждый тик.

Scientific core-метрики остаются включёнными при `EnableCounters=false`;
флаг отключает только legacy-счётчики.

### 5.4 Явно неподдерживаемое

`S3i` зафиксировал scope: keep-alive/PING, UNSUBSCRIBE, retained store/replay и
session resume с DUP не моделируются и объявлены явно. Это лучше, чем молча
терять сообщения. Подробности — в истории S3i (`git log --grep=S3i`).

## 6. Эксперименты и результаты

| Компонент | Ответственность |
|---|---|
| `RuntimeOptions` | снимок `MQTTY_*` переменных, один источник для workers/barrier/chunk/diagnostics |
| `JsonPolicies` | единые `JsonSerializerOptions` для результатов |
| `JsonResultWriter` / `ResultSchema` | версионированная схема, сортировка, `sha256.json` (seal не включает сам себя) |
| `MetricsCollector` | typed core + diagnostics, сравнение seq/par |
| `Experiment.Cli` | единый запуск LB/Relay/Fed на продуктовых движках |
| `Benchmark.Compare` | reference seq/par, медианы, CV, gating |

`ResultSchema` и `sha256.json` — часть контракта воспроизводимости: экспорт
сортирован, а любой артефакт можно проверить по seal. `sha256.json` никогда не
входит в собственный список хешей.

## 7. Приложения

### Web

- мутации вынесены в application-слой (`SimulationCreationHandler`,
  `SimulationJobHandler`, `SaveTopologyHandler`, `TwinValidationHandler`);
- `JsonFileStore` делает атомарную запись (temp + move) и версионирует документы
  (`SchemaVersion` + миграции);
- security: опциональный API key, fixed-window rate limit, HTTPS/HSTS, CORS;
  `SecurityOptions` зарегистрирован и как singleton, и как `IOptions<>`;
- фоновые job'ы: in-process и out-of-process, отмена/пауза/лимит параллелизма.

### Designer и DesignDocument

`EntityFX.MqttY.Design` — UI-независимый слой:

- POCO `DesignDocument`/`DesignNetwork`/`DesignNode`/`DesignLink`/
  `DesignNetworkType`;
- двусторонний маппинг `NetworkGraphOption ↔ DesignDocument`, JSON save/load;
- `IDesignLayoutEngine` + детерминированный `GridDesignLayoutEngine`;
- `GraphMlDesignReader` — GraphML → `DesignDocument` без Avalonia/ReactiveUI.

Avalonia-модели (`NetworkDesignModel`, `NodeDesignModel`, …) — тонкие адаптеры:
`ToDesignDocument`/`FromDesignDocument`, а `ToNetworkGraphOption`/
`FromNetworkGraphOption` делегируют в общий слой. `GraphMLImporterService`
содержит только вызов reader'а. При load/save сохраняются `Broker`,
`Additional` и `SendTicks`.

## 8. Статус переписывания S0–S5

| Этап | Что сделано | Проверка |
|---|---|---|
| **S0** | golden packet trace, детерминизм seq/par | characterization tests |
| **S1** | швы `ISimulationClock`, `TopologySnapshot`, `TickContext`, `IAdmissionPolicy` | 392/392 на момент этапа |
| **S2** | data-oriented слоты вместо wrapper/pool; path-массив + NextHop | транспортные тесты |
| **S3f** | раздельные inbound/outbound namespace, FSM сессии | 5 FSM-тестов |
| **S3g** | кодек Span, кэш topic/filter, envelope QoS0 | аллокации −62…−70 % |
| **S3h** | admission/metrics вне протокола, dense latency histogram | `BrokerAdmissionTests` |
| **S3i** | явный scope неподдерживаемого протокола | документировано |
| **S4d** | единая result schema и seal | `JsonResultWriterTests` |
| **S4e** | `RuntimeOptions` + `JsonPolicies` | `RuntimeOptionsTests` |
| **S4f** | единый CLI на продуктовых движках | build + smoke |
| **S4g** | `MetricsCollector` | `MetricsCollectorTests` |
| **S5d** | Web application handlers | Web tests |
| **S5e** | versioned persistence, атомарная запись | `VersionedPersistenceTests` |
| **S5f** | API key, rate limit, HTTPS/HSTS, CORS | `SecurityTests` |
| **S5g** | `DesignDocument`, layout, GraphML, адаптеры Designer | `DesignDocumentTests`, `DesignerModelTests` |

## 9. Тесты и проверка

```bash
# Интеграционные
dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Integration/EntityFX.Tests.Integration.csproj -c Release --nologo

# Web (в этом окружении restore требует отключения проверки подписи NuGet)
DOTNET_NUGET_SIGNATURE_VERIFICATION=false \
  dotnet test src/EntityFX.MqttSimulator/EntityFX.Tests.Web/EntityFX.Tests.Web.csproj -c Release --nologo
```

Текущее состояние: Integration **436/436**, Web **62/62**.
Golden-трассы S0 сравниваются в `CharacterizationPacketTraceTests`; benchmark
дополнительно проверяет `core=match` между seq и par.

## 10. Что не переписывать

- Формат golden-трасс и семантику `Click`/`Refresh`/`PassTillNextTick`.
- Порядок side-effect'ов, от которого зависят core-метрики (порядок фаз,
  сортировка fanout, seeded RNG).
- Контракты внешних приложений и экспортируемую схему результатов без
  `SchemaVersion`.
- `ConcurrentBag` packet pool: per-node `ConcurrentStack` даёт регрессию.

## 11. Дальнейшие работы

Переписывание завершено; остаток — производительность. Актуальные приоритеты,
отклонённые эксперименты и фазовый профиль — в [performance.md](performance.md).
Главные незакрытые точки: инфляция работы узлов (~1.8× в параллели) и
Federation drain (~50 % wall), `M7`/`M8` дают ниже уровня шума.
