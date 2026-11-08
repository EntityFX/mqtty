# Производительность MqttY

Документ фиксирует методику замеров, текущие результаты, принятые и отклонённые
оптимизации и остаточные узкие места. Устройство проекта — в
[architecture.md](architecture.md).

## 1. Стенд и протокол

**Текущий хост:** Intel Xeon E5-2680 v3, **12 физических / 24 логических**
ядра, 30 МБ L3, Linux, .NET 6 runtime. Reference-замеры — `workers=12`.

```bash
# LB L: 64 брокера, 4 сегмента, 256 publishers, 20k rps, 3 с
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj \
  -c Release --no-build -- --engines lb --repeat 7 --warmup-runs 1 --measure 3 --drain 10 \
  --workers 12 --brokers 64 --nets 4 --publishers 256 --rps 20000 --no-export --no-strict

# Relay: 2k rps
dotnet run ... -- --engines relay --repeat 7 --warmup-runs 1 --measure 3 --drain 10 \
  --workers 12 --fed-rps 2000 --no-export --no-strict

# Federation L: 16 брокеров, 256 publishers, 2k rps, 1 с
dotnet run ... -- --engines fed --repeat 5 --warmup-runs 1 --measure 1 --drain 10 \
  --workers 12 --brokers 16 --publishers 256 --fed-rps 2000 --no-export --no-strict
```

Правила:

- прогрев ≥ 1 прогон, затем медиана из ≥ 5 повторов, порядок seq/par
  чередуется;
- `--no-export` для гейта, экспорт проверяется отдельно;
- `--gate-min-wall X` / `--gate-min-refresh X` — жёсткий порог в strict-режиме
  (exit code 3 при недоборе); без флагов порог не применяется;
- `core`-метрики seq и par обязаны совпадать; `diagnostics` — по возможности
  (у Federation исторически расходятся и на baseline);
- CV > 5 % — повторить серию; шум на текущем хосте достигает ±3–5 %.

### Гейты

| Гейт | Критерий | Статус |
|---|---|---|
| G1 | refresh ≥ 3.5× (4C/8T) / ≥ 4× (8 физ.) | LB L 6.0×, Relay 6.3×, Fed 4.9–5.3× — да; LB ref 3.4× — нет |
| G2 | end-to-end ≥ 3.5× / ≥ 4× | LB L 4.98×, Relay 5.26× — да; Fed 3.9–4.2× — на границе; LB ref 2.98× — нет |
| G3 | efficiency(physical) ≥ 50 % | LB L 42 %, Relay 44 %, Fed 33–35 % — нет |
| G4 | seq == par, `errors=0` | core match везде; Fed diagnostics differ (pre-existing) |
| G5 | масштабный конфиг ≥ 4× | LB L / Relay — да, Fed — на границе 3.97–4.14× |
| G6 | 0 новых падений | Integration 436/436, Web 62/62 |

## 2. Текущие результаты

12C/24T, workers=12, `--no-export`, медианы:

| Конфиг | seq | par | wall speedup | refresh speedup | core |
|---|---:|---:|---:|---:|---|
| **LB L** (64 брокера, 256 publishers, 20k rps) | 9.24 с | **1.86 с** | **4.98×** | **6.00×** | match |
| **LB reference** (16 брокеров, 64 publishers, 10k rps) | 1.92 с | **0.64 с** | **2.98×** | **3.36×** | match |
| **Relay** (2k rps) | 11.97 с | **2.27 с** | **5.26×** | **6.28×** | match |
| **Federation L** (16 брокеров, 2k rps) | 13.5–14.3 с | **3.4–3.8 с** | **3.9–4.2×** | **4.9–5.3×** | match / diag differ |

Скорость seq/par на LB L выросла с ~3.9× до **5.06×** после двух изменений
этого цикла: порядок сетей и GC-настройка бенчмарка.

## 3. Что дало ускорение

### 3.1 Планирование и синхронизация

- **Постоянный пул воркеров** вместо `Task`/`Parallel.For` на каждый тик.
- **Двухфазный refresh** (сети → fence → узлы → fence); стратегия «сеть + её
  узлы» оставлена только для sequential.
- **Spin/monitor auto-choice**: spin только когда `workers ≤ physical cores`,
  иначе BCL `Barrier`, чтобы SMT-переподписка не голодала воркеры.
- **Динамический захват работы**: узлы по одному (`nodeChunk=1`), сети — по
  нескольким; тяжёлый брокер не тащит за собой чанк лёгких узлов.
- **Порядок сетей по плотности (2026-11-08).** Общая client-LAN (320 узлов)
  сортировалась по имени после backend-сетей и часто попадала в конец выдачи.
  В параллельной фазе 1 сети теперь стартуют с самых плотных (tie — имя);
  последовательный порядок не меняется. Эффект LB L: barrier sum
  `6.2–7.1 с → 4.8 с`, refresh wall `1.99–2.13 → 1.92–1.94 с`, end-to-end
  `2.492 → 2.401 с` (−3.7 %, repeat 7); Relay `3.83 → 3.69 с`.

### 3.2 GC и фиксированные расходы (2026-11-08)

Фазовый профиль (`MQTTY_PHASE_STATS=1`) показал, что в LB L `connect` занимал
0.6–0.8 с из ~2.4 с, причём внутри — блокирующие gen0/gen1/gen2-паузы,
собирающие мусор предыдущего сэмпла. Сделано:

- между сэмплами бенчмарка выполняется полный blocking GC **вне** измеряемого
  участка;
- эталонный бенчмарк и единый CLI запускаются с `ServerGarbageCollection=true`
  и `ConcurrentGarbageCollection=false`.

| Конфиг | wall до | wall после | speedup до | speedup после |
|---|---:|---:|---:|---:|
| LB L | 2.49 с | **2.02 с** | 3.93× | **5.06×** |
| LB reference | 0.83 с | **0.64 с** | 2.20× | **2.98×** |
| Relay | 3.69 с | **2.73 с** | 4.19× | **5.44×** |

### 3.3 Ядро и транспорт

- Data-oriented `NodePacketSlot`/`NetworkPacketSlot` вместо wrapper-классов и
  object pool; path-массив + `NextHop` без копирования на каждом hop.
- `CounterClock`: один publish `(tick, step)` на тик вместо обхода дерева
  счётчиков; кэширование массивов `Counters`.
- `TokenWindowAdmission` на одном `Interlocked` claim; fast-fail по длине
  очереди до захвата бюджета.
- `TopologySnapshot` и `CurrentRefreshSteps` кэшируются на тик.
- `InMemoryRepository` не пересобирает snapshot, если MQTT обновил тот же
  экземпляр сессии.

### 3.4 MQTT

- Кодек на `Span<byte>`/`BinaryPrimitives`; кэш topic/filter; envelope QoS0
  один на публикацию. Аллокации Relay L: `75.8 → 29.0 ГБ` (−62 %), Federation
  seq: `44.5 → 13.4 ГБ` (−70 %) на момент S3g.
- `AnyPendingMessage` / `FindPendingAcknowledgement` без `ToArray()` под lock
  (QoS2): seq −9 %, par −2–5 %, GC −3.8 % на 4C/8T.
- Предсортированный снапшот подписок брокера; `PacketIdProvider` lock-free.
- `BrokerMeasurement` и `IBrokerAdmissionModel` вынесены из протокола;
  latency-гистограмма — dense tick-массив.

### 3.5 Federation

- `FederationDeliveryLedger`: `ConcurrentDictionary` + per-receiver lock, SHA
  вне глобального lock, `Receipts()` сортируется один раз на снапшоте.
- `StaticFederator`: предвычисленные targets/queues, `Pump` без LINQ,
  `IsIdle` — O(1) + кэш на тик.
- **Параллельный Pump на пуле тика (2026-11-08).** Профиль показал, что
  `topology.Pump()` занимал ~2.5 с из ~7 с wall Federation: при 2k rps и
  16 сайтах одно сообщение порождает ~240 пересылок, то есть ~46 publish на
  тик, и всё это шло последовательно на вызывающем потоке. Добавлен
  `TickWorkerPool.RunSingle` (одна фаза + один fence) и
  `FederationTopology.Pump(parallel)`: федераторы владеют раздельными
  output-клиентами и очередями, поэтому качаются параллельно на том же
  постоянном пуле. Результат: Fed L wall `7.06 → 3.7–3.8 с`, speedup
  `2.12 → 4.0–4.2×`, refresh `3.03 → 5.0–5.3×`. Дробление работы до
  пар «федератор × target» выигрыша не дало (лишние 5 млн claim'ов) и
  откатано. Старый `Parallel.For` на каждый тик по-прежнему не используется.
- **Хеш payload один раз на публикацию.** `TransmissionLedger.Observe` считал
  SHA-256 на каждую из ~1 млн пересылок; теперь хеш считается в
  `FederationDeliveryLedger.Create` и переиспользуется в transmission/receipt
  записях. Fed L: wall `3.74 → 3.44 с` (−8 %), speedup `4.00 → 4.16×`,
  refresh `5.01 → 5.33×`; Relay без регрессии.

### 3.3a Пустые узлы (2026-11-08)

`Node.Refresh` обрабатывал две пустые `ConcurrentQueue` и проворачивал пустые
deferred-списки на каждом узле каждый тик. Добавлен early-out: если обе
очереди и все deferred-списки пусты, счётчики обновляются, а ротация
пропускается. Абсолютные времена: LB L `1.99 → 1.86 с`, Relay `2.80 → 2.27 с`,
Fed seq `14.3 → 13.5 с`; `core=match` во всех режимах. Speedup-ratio почти не
меняется, потому что выигрывают и seq, и par (для Fed ratio 3.9–4.2× — в
пределах шума).

## 4. Фазовый профиль

### LB L (parallel)

| Фаза | Wall |
|---|---:|
| construct | 0.13–0.20 с |
| connect | 0.47–0.63 с |
| subscribe | ~0.001 с |
| warmup | ~0.005 с |
| measure | 1.29–1.55 с |
| drain | ~0.003 с |

Внутри measure (типичный прогон, 12 воркеров): phase1 ≈ 2.6–2.9 с суммы,
phase2 ≈ 13.9–14.5 с суммы, barrier ≈ 4.8–5.2 с суммы, wall ≈ 1.9–2.0 с.
После оптимизации сетей CPU-эффективность ~95 % — потолок BSP-схемы.

### Federation L

| Фаза | Wall |
|---|---:|
| measure (1 с = 10 000 тиков) | 1.35–1.60 с |
| drain (~10 650 тиков) | 1.59–1.65 с |

После параллельного Pump measure и drain почти сравнялись: дренаж — это
~10.6k тиков того же per-tick refresh, а не простой. `IsIdle` не виноват:
полный скан графа стоит ~6 мкс на вызов (~60 мс на 10 000 тиков).
Диагностика фаз передаётся отдельным `phaseWallSeconds`, чтобы не менять
экспортируемый `phases.json`.

## 5. Отклонённые эксперименты

Правило: этап, не давший измеримого эффекта, откатывается, а не «докручивается».

| Эксперимент | Результат |
|---|---|
| Pipeline «network + outgoing» | 1.44× против 1.55× |
| Per-network / strategy 1 параллельно | хуже плоского two-phase |
| Cost-aware LPT для узлов | 1.38× против 1.61× |
| Параллельный `Pump` через `Parallel.For` на каждый тик | 1.52× против 1.98× (старый вариант); заменён пулом тика |
| Дробление Pump до пар «федератор × target» | без выигрыша, лишние claim'ы; откатано |
| Ingress queue sharding | хуже; шардирование не окупается |
| Односторонний финальный fence | зависание / 3.21 с |
| Параллельные connect/subscribe | нет стабильного выигрыша |
| Параллельный publish | 3.33× против 3.81× |
| Non-atomic packet id | без выигрыша |
| Active-only refresh | LB: par ~нейтрально, seq +7 % из-за проверки; Relay/Fed хуже — оставлено выключенным |
| `nodeChunk > 1` | хуже из-за хвоста тяжёлых узлов |
| `MQTTY_ISOLATE_NETWORKS=1` | абсолютно быстрее, но seq падает сильнее; speedup ниже, по умолчанию выключено |
| Spin-limit sweep 0…2048 / `SpinWait(128)` | без эффекта; барьер ждёт дисбаланс, а не fence |
| Per-node `ConcurrentStack` вместо `ConcurrentBag` | +16 % refresh par |
| `_responseMessages` → `Dictionary` | маржинально: таблица пуста в измеряемых сценариях |
| O(1)-счётчики pending + `T[] ReadAll` | без измеримого выигрыша; откатано |
| Path-cache без копирования | ~0.5 % аллокаций, wall без изменений |
| Передача parsed `PublishPacket` через `Context` | ~1 % аллокаций, wall без изменений |
| Отключение delivery-метрик | ~2 % node-работы, не окупает изменение семантики |
| `MQTTY_DISABLE_COUNTERS=1` | seq node −14 %, parallel node без изменений |

## 6. Остаточные узкие места

1. **Инфляция работы узлов ~1.8×** (Client ~1.85×, Server ~1.35×): параллель
   делает ту же работу дороже из-за кэшей/GC/разделяемых структур. Это главный
   резерв; снижение требует data-oriented изменений в протокольном hot path,
   а не планировщика.
2. **Federation drain** — длинный модельный хвост (~10.6k тиков) с той же
   стоимостью тика, что и measure; после параллельного Pump wall упал до
   ~3.7–3.8 с, но drain всё ещё ~45 % от него. Резерв — per-tick работа
   брокеров (входящий publish ~8 мкс/пакет в измеренном профиле).
3. **Барьер/хвост** ~25–30 % refresh wall: после сортировки сетей это в
   основном дисбаланс тяжёлых узлов; `M7` (адаптивный барьер) даёт < 1 % и
   закрыт как отклонённый. `M8` (`_responseMessages`) маржинален: таблица
   пуста в измеряемых сценариях.
4. **Аллокации** ~1.6–2 ГБ на LB L par-прогон; Server GC убрал паузы из
   критического пути, дальнейшее — pooling payload/слотов.
5. **LB reference** 2.98× — малая топология упирается в фиксированные расходы
   и барьер, а не в объём работы.

## 7. Диагностические переключатели

| Переменная | Назначение |
|---|---|
| `MQTTY_WORKERS` | число воркеров |
| `MQTTY_BARRIER=spin\|monitor` | принудительный тип fence |
| `MQTTY_BARRIER_SPIN` | spin-limit барьера |
| `MQTTY_NODE_CHUNK` / `MQTTY_NETWORK_CHUNK` | размер чанка захвата |
| `MQTTY_PARALLEL_PUBLISH` | параллельный publish в `Drive` (по умолчанию off) |
| `MQTTY_ISOLATE_NETWORKS` | отдельная client-LAN на backend (не для гейта) |
| `MQTTY_DISABLE_COUNTERS` | отключить legacy-счётчики |
| `MQTTY_ACTIVE_ONLY` | пропускать простаивающие узлы |
| `MQTTY_REFRESH_STATS` | wall/phase/barrier по тикам |
| `MQTTY_WORKER_STATS` | phase1/phase2/barrier по воркерам |
| `MQTTY_NODE_STATS` | время и аллокации по узлам/типам |
| `MQTTY_PHASE_STATS` | wall/refresh по фазам LB и Fed |
| `MQTTY_NONATOMIC_ID` | неатомарный packet id (только диагностика) |

## 8. История итераций

Историческая серия на i7-3770 (4C/8T) и demo-конфигах; напрямую не сравнивать
с текущими reference-цифрами.

| Дата | Сценарий | Было | Стало | Что сделано |
|---|---|---:|---:|---|
| 2026-11-01 | LB demo | 9.28 с | ~1.96 с | профилирование, кэш counters, параллельный refresh |
| 2026-11-04 | LB demo | 1.96 с | 0.74 с | `CounterClock`, фиксированный пул, снапшот подписок |
| 2026-11-01 | Relay original | 24.81 с | 9.77 с | object pooling, guard monitoring, Step per transfer |
| 2026-11-01 | Federation ring | 6.25 с | 1.24 с | lock-free ledger, Pump без OrderBy, IsIdle cache |
| 2026-11-08 | Fed L reference | 7.06 с | 3.75 с | параллельный Pump на пуле тика, `RunSingle` |
| 2026-11-08 | Fed L reference | 3.74 с | 3.44 с | SHA-256 payload один раз на публикацию |
| 2026-11-08 | LB L / Relay | 1.99 / 2.80 с | 1.86 / 2.27 с | early-out пустых очередей узла |

Эти цифры — про разные конфигурации и железо; для приёмки используйте только
reference-бенчмарк из §1–2.

## 9. Правила изменений

- Сначала измерение, потом изменение; после изменения — тот же прогон.
- Регрессия wall/refresh > 5 % или `core=MISMATCH` — откат.
- Не оптимизировать «на глаз»: scheduling-эксперименты уже дали отрицательные
  результаты (см. §5).
- Любое изменение экспортируемой схемы требует `SchemaVersion` и обновления
  seal-тестов.
