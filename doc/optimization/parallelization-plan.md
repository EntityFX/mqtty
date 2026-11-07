# План: масштабирование параллельного режима MqttY

**Цель:** получить устойчивое ускорение `parallel=true` относительно `parallel=false` на 8 физических ядрах — **4–8×** — без изменения научных метрик.

**Статус:** baseline зафиксирован (ветка `develop`, `772a37e` + текущие незакоммиченные оптимизации). Документ — рабочий план, не отчёт.

---

## 1. Критерии приёмки

| Гейт | Метрика | Порог |
|---|---|---|
| **G1. Refresh** | `median(seq_refresh / par_refresh)`, 5 прогонов | ≥ 4× на 8 физ. ядрах; ≥ 3.5× на текущем 4C/8T |
| **G2. End-to-end** | `median(seq_wall / par_wall)`, measurement ≥ 3 с, экспорт выключен | ≥ 4× на 8 физ.; ≥ 3.5× на 4C/8T |
| **G3. Масштабирование** | `speedup(physical_cores) / physical_cores` | ≥ 50 % |
| **G4. Детерминизм** | core: counts, delivered, goodput, publish p50–p99; diagnostics: receiver latency, queueDrops | core совпадает точно; diagnostics могут отличаться и не валят гейт |
| **G5. Масштаб** | большая топология, ≥ 3 с модельного времени | ≥ 4×, `status=completed`, `errors=0` |
| **G6. Тесты** | интеграционные | 0 новых падений |

> **Про железо.** На текущем хосте `nproc=8`, но `Thread(s) per core = 2`, то есть это **4 физических ядра / 8 потоков**. Потолок здесь ~4–4.5×: SMT не даёт второго ядра. Поэтому «4–8×» — цель для 8 физических ядер; на этом хосте приёмка — **3.5–4.5×**. Гнаться за 6–8× на 4C/8T бессмысленно.

---

## 2. Фактическая картина (baseline)

Замеры на текущем хосте, reference-бенчмарк `EntityFX.MqttY.Benchmark.Compare`:

| Движок | seq, с | par, с | speedup | Доля ожидания в барьере | `p2_parallelism` |
|---|---:|---:|---:|---:|---:|
| LoadBalancing | 1.52 | 1.01 | **1.5×** | **61 %** | 7.04 / 8 |
| Relay | 7.90 | 3.03 | **2.6×** | — | — |
| Federation | 27.2 | 16.4 | **1.65×** | 26 % | 7.85 / 8 |

`p2_parallelism` = сумма занятости воркеров в фазе узлов / максимум одного воркера. Значение 7–7.85 означает: **сама работа параллелится почти идеально**. Потери — в синхронизации и последовательных участках.

### 2.1 Кривая масштабирования (текущий код)

`DOTNET_PROCESSOR_COUNT=n`, один и тот же reference-конфиг:

| Логических CPU | LoadBalancing | Relay | Federation |
|---:|---:|---:|---:|
| 1 | 1.16× | 1.10× | 1.12× |
| 2 | 1.93× | 1.65× | 1.57× |
| 4 | **1.99×** | 2.06× | **1.81×** |
| 8 | 1.61× | 2.49× | 1.67× |

Выводы:
- уже при 1 CPU методика даёт +10–16 % (второй прогон теплее) — это надо вычитать из всех цифр;
- **LB на 8 потоках медленнее, чем на 4**: стоимость `Barrier` растёт с числом участников, а SMT не даёт второго ядра;
- Relay масштабируется лучше остальных, Federation упирается в последовательные участки уже на 4;
- «максимальное число ядер» = **физические ядра**; на 4C/8T оптимум для текущего кода — 4 воркера. На 8 физических ядрах повторить замер 8 воркеров.

### 2.2 Где именно теряется время

1. **`System.Threading.Barrier` вызывается дважды на каждый тик** (`TickWorkerPool.ExecuteTick`). На LB это ~61 % занятости воркеров: работа тика — десятки микросекунд, а барьер стоит ~40 мкс на воркера. Стоимость растёт с числом участников — поэтому 8 потоков хуже 4. Это главный тормоз.
2. **Federation: последовательная часть вне `Graph.Refresh`** — ~6.6 с из 16.4 с. Внутри:
   - `StaticFederator.Pump()` делает `outputs.OrderBy(...)` **на каждый вызов**, то есть сортировку на каждом тике для каждого федератора;
   - `topology.Pump()` обходит федераторы последовательно;
   - `topology.IsIdle` в drain сканирует все сети/узлы/федераторы на каждом тике.
3. **`FederationDeliveryLedger` — глобальный `lock`** на каждый `Create`, `Find`, `Receive`. Внутри `Receive` под тем же локом считается `SHA-256` на каждую доставку. Это сериализует всю федерацию независимо от числа ядер.
4. **`TotalSteps` перебирает `ThreadLocal.Values`**; `NodeBase.Refresh` вызывает его на каждом узле каждого тика. Лишняя работа в горячем пути.
5. **`Parallel.For` на каждый тик в `Drive()`** — планировщик пула потоков на каждом тике; для 64 публикаторов это часто дороже самой публикации.
6. **Экспорт результатов входит в wall-clock** (`publications.json`, `transmissions.json`, сортировка десятков тысяч receipt'ов) и одинаков для seq/par — он «съедает» ускорение.

### 2.3 Почему текущий бенчмарк не годится как гейт

- measurement = 0.5 с; фиксированные construct/connect/subscribe/export = 0.27–0.57 с. Даже при нулевой стоимости refresh Amdahl даёт потолок ~2.7× для LB.
- в старом прогоне seq всегда запускался первым, par — вторым: второй прогон получал прогретый JIT/кэш. При `DOTNET_PROCESSOR_COUNT=1` (где true speedup = 1.0) старый бенчмарк показывал **1.10–1.16×**; в новом harness порядок чередуется.
- Federation `DrainTimeoutSeconds = 1` → `status=failed` (таймаут drain), и сравнение идёт по обрезанной работе.
- Нет разбивки по фазам и нет кривой масштабирования по числу воркеров.

### 2.4 Результаты M0/M1

M0 выполнен: reference-бенчмарк получил CLI-параметры, прогревочный прогон, чередование
seq/par, медиану, JSON, разделение core/diagnostics и отдельный учёт refresh/export.

M1 (барьер) проверен A/B на 4 физических ядрах, `MQTTY_DISABLE_COUNTERS=1`, measure 0.5 с:

| Барьер | seq wall | par wall | par refresh | refresh speedup |
|---|---:|---:|---:|---:|
| `monitor` (`System.Threading.Barrier`) | 0.51 с | 0.64 с | 0.475 с | 1.01× |
| `spin` (sense-reversing) | 0.51 с | 0.50 с | 0.360 с | 1.20× |

Вывод: спин-барьер быстрее на ~20–25% и включён по умолчанию, когда
`workers ≤ physical cores`; при переподписке (SMT) используется `Barrier`, иначе
спиннеры голодают. По умолчанию воркеров теперь ровно по физическим ядрам,
`MQTTY_WORKERS`/`MQTTY_BARRIER` — override.

Но главный тормоз оказался не в сигнализации барьера. На gate-конфиге (4 воркера,
measure 5 с) суммарная работа воркеров составила ~6.2 с при последовательном refresh
~3.36 с: **работа в параллели дороже в ~1.8×**. Это конкуренция за общие структуры
сети: `ConcurrentQueue` и `Interlocked` на `_windowBytes`/`_queueLength`, общий
`GetPacketId`, отправка многих узлов в один и тот же shared LAN.

Gate после M0/M1/M2 (4C/8T, прогретые медианы):

| Движок | wall speedup | refresh speedup | core | diagnostics |
|---|---:|---:|---|---|
| LoadBalancing | 1.65× | 1.78× | match | match |
| Relay | 2.35× | 2.47× | match | match |
| Federation | 1.98× | 2.49× | match (не overload) | differ |

Что дал M2:
- убран `Parallel.For` на каждый тик в `Drive` (50k вызовов): parallel сжигал **6× CPU** и давал LB 1.0×. Serial publish по умолчанию: LB **1.0 → 1.5×**, CPU **6× → 2.5×**; `MQTTY_PARALLEL_PUBLISH=1` — opt-in для больших топологий.
- `MQTTY_NODE_CHUNK` по умолчанию 1 вместо ~5: +10–15% (хвост тяжёлого брокера больше не тащит воркер).
- tuple-ключ в `DijkstraPathFinder` вместо строки на каждый lookup: меньше аллокаций.
- node-stats профиль: инфляция per-call равномерна по Server/Client (~1.4–1.5×); packet id, packet pool и изоляция сетей эффекта не дали. Pointer-chasing 1→4 потока масштабируется на 96%, то есть память не предел.
- остаётся: барьер ~27% wall при 4 воркерах, инфляция работы ~1.4×, ~5 ГБ аллокаций на прогон.

Federation при перегрузке (`queueDrops > 0`) теряет детерминизм по `unique`/`queueDrops`:
конфиг для гейта должен быть не overload.

### 2.5 Отклонённые эксперименты

| Эксперимент | Результат | Причина |
|---|---|---|
| Параллельный `Pump` через `Parallel.For` | 1.52× vs 1.98× | per-tick `Parallel.For` дороже работы |
| Cost-aware статическое распределение узлов (LPT) | 1.38× vs 1.61× | динамический work stealing лучше держит per-tick разброс |
| Конвейер: network N + node-outgoing N параллельно, затем incoming | 1.44× vs 1.55× | обе фазы CPU-bound и уже используют все ядра; перекрывать нечего |
| Active-only на large LB | 1.97× vs 2.19× | большинство узлов активны, проверка quiescent дороже пропуска |
| Fast-idle пустых узлов/сетей | 1.66× vs 1.65×; 2.29× vs 2.29× | `ProcessReady` на пустых очередях уже дёшев |
| 8 логических потоков (4C/8T) | small 0.86×/0.90×, large 2.11×/2.39× | SMT: 4 физических ядра лучше 8 логических |
| Per-network dependency scheduler | small 1.51× vs 1.59×, large 1.94× vs 2.25× | ready-queue + spin дороже снятого глобального barrier |

Вывод: оставшийся разрыв до 3.5–4.5× — это не «плохая синхронизация», а
**объём CPU-работы на тик**. Дальнейший рост возможен только через уменьшение
работы (event-driven/более грубая дискретизация) или больше физических ядер.

### 2.6 Почему не масштабируется: профиль

Замеры parallel-only (`--mode par`) и sequential (`--mode seq`), LB, measure 10 с,
4 воркера, счётчики off:

| Режим | wall | CPU | CPU/wall | refresh |
|---|---:|---:|---:|---:|
| sequential | 8.01 с | 7.85 с | 0.98 core | 6.72 с |
| parallel 4 | 5.33 с | 17.80 с | 3.33 core | 4.02 с |

Фазовый бюджет parallel:
- `phase1 + phase2` = 2.93 + 7.18 = **10.12 с** работы → 2.53 с при идеальных 4 воркерах;
  последовательный refresh — 6.72 с, то есть **инфляция работы ×1.51**;
- `barrier` = 3.93 с aggregate → 0.98 с на воркера;
- `p2_parallelism` = 3.94/4; средняя загрузка 3.33 из 4 ядер (83%).

CPU-трассировка parallel-only (top exclusive):
- `TickWorkerPool.WaitForNextGeneration` — **31.2%**;
- `SpinBarrier.SignalAndWait` — **19.1%**;
- `Node.Refresh` inclusive — 28.9%;
- `EnumerableHelpers.ToArray` — 6.4%;
- `StorageExtensions.GetPendingMessages` — 3.4%;
- `ConcurrentDictionary.TryAddInternal` — 2.3%;
- `BrokerMeasurement.*` — ~4%.

То есть **~50% времени — ожидание/спин**, а не полезная работа. После удаления SHA
из ideal-пути и per-session locks абсолютный CPU упал на 7–9%, но ratio не вырос:
быстрее стали обе стороны.

Проверки, исключающие железо:
- pointer-chasing 1→4 потока: per-thread 96%;
- OpenSSL 4 потока: per-thread 92%;
- 8 логических потоков на 4C/8T хуже 4.

Итоговая модель:
- zero barrier → refresh ≈ 2.8× (упирается в инфляцию работы);
- zero work inflation → refresh ≈ 2.5× (упирается в барьер/сериализацию);
- для 4× нужно **и то, и другое**; на 4C/8T потолок ~2.5–3×.

---

## 3. Целевая архитектура

- Один постоянный пул воркеров на симуляцию, без создания задач на тик.
- Дешёвый fence вместо `Barrier`: sense-reversing barrier на `Interlocked` + spin, с парковкой только при долгом ожидании.
- Две фазы тика: сети → узлы. Конец тика — отдельный fence; начало следующего тика не пересекается с узлами предыдущего.
- Каждая сущность пишется одним воркером за тик; общие структуры — read-only снапшоты.
- В горячем пути нет глобальных блокировок и LINQ-аллокаций.
- Счётчики/мониторинг — опциональны и сэмплируются; не влияют на гейт.
- Адаптивный размер чанка и число воркеров (physical cores vs logical).

---

## 4. Работы по этапам

### M0. Reference benchmark и протокол измерений (P0, 2–3 дня) — ВЫПОЛНЕНО

Файл: `EntityFX.MqttY.Benchmark.Compare/Program.cs`.

- Аргументы: `--engines`, `--workers`, `--repeat`, `--measure`, `--drain`, `--warmup`, `--rps`, `--brokers`, `--publishers`, `--no-export`, `--json`.
- Мерить отдельно: construct, connect, subscribe, warmup, measure, drain, export, `refresh-only`, `publish`.
- Прогревочный прогон; чередовать порядок seq/par; медиана из 5; выводить min/median/max/CV.
- Сравнивать summary seq/par (гейт G4) и падать при расхождении.
- Зафиксировать baseline в этом документе.
- **Prerequisite (выполнено):** оба baseline-падения исправлены, полный набор 388/388.

**Приёмка:** CV < 5 %; при `--workers 1` speedup = 1.00 ± 0.05 (факт: 0.95–1.00).

### M1. Дешёвый tick fence вместо `Barrier` (P0, 2–3 дня) — ВЫПОЛНЕНО (частично)

Файлы: `Network/TickWorkerPool.cs`, `Network/NetworkSimulator.cs`.

- Заменить `System.Threading.Barrier` на sense-reversing barrier: `Interlocked.Increment` + spin, при долгом ожидании — `ManualResetEventSlim`/`Monitor.Wait`. Не потерять full fence: `Interlocked`/`Volatile` должны обеспечивать видимость записей фазы 1 в фазе 2.
- Убрать широковещательный `Monitor.PulseAll` на каждый тик; будить воркеров адресно или через spin-поколение.
- Слить финальный fence тика с публикацией следующего тика.
- Добавить `MQTTY_WORKERS` и сравнить 4 (physical) vs 8 (logical): на SMT барьер может быть дороже, чем выигрыш; текущий замер это уже подтверждает (LB 1.99× на 4 vs 1.61× на 8).
- Оставить fallback на последовательный refresh для маленьких топологий.

**Приёмка:** ожидание в барьере < 10 % refresh wall; LB refresh ≥ 2× (цель 3×).

**Факт:** спин-барьер дал ~20–25% на 4 физических ядрах, но порог <10% не достигнут из-за
SMT/дисбаланса и конкуренции за общие структуры. Оставлен автовыбор
`spin`/`monitor` и дефолт по физическим ядрам. Дальнейшее снижение барьера — только
вместе с M2 (меньше работы в тике) и уменьшением числа fence'ов.
**Конвейер** (network + outgoing, затем incoming) проверен и отклонён: 1.44× против 1.55×,
потому что обе фазы CPU-bound и используют все ядра.

### M2. Убрать per-tick overhead и лишнюю работу (P0, 3–4 дня) — ВЫПОЛНЕНО (частично)

Файлы: `LoadBalancingExperimentRunner.cs`, `FederationRunner.cs`, `NetworkSimulator.cs`,
`TickWorkerPool.cs`, `PathFinder/DijkstraPathFinder.cs`.

Что сделано:
- **Убран `Parallel.For` на каждый тик в `Drive`** (50k вызовов): он сжигал ~6× CPU
  и ограничивал LB 1.0×. Serial publish по умолчанию: LB 1.0 → ~1.5×, CPU 6× → ~2.5×.
  `MQTTY_PARALLEL_PUBLISH=1` — opt-in для очень больших топологий.
- **`MQTTY_NODE_CHUNK` = 1 по умолчанию**: тяжёлый брокер больше не тащит за собой
  чанк лёгких узлов; +10–15%.
- **tuple-ключ в `DijkstraPathFinder`** вместо строки на каждый lookup.
- **`MQTTY_NODE_STATS=1`** — per-node/per-type профиль.
- **Federation**: SHA-256 вынесен из глобального lock, порядок `Pump` предвычислен,
  `IsIdle` кэшируется на тик.
- **`DeliveryLedger` LB** переведён на lock-free структуры: `ConcurrentDictionary`,
  per-backend counters/histograms и Interlocked; глобальный lock на каждую доставку убран.
- **`TotalSteps`** кэшируется на тик (`CurrentRefreshSteps`), узлы/сети больше не
  перебирают `ThreadLocal.Values`.
- **Active-only refresh** (`MQTTY_ACTIVE_ONLY=1`) реализован, но по умолчанию выключен:
  на плотной LB-нагрузке большинство узлов активны, выигрыша нет.
- Тесты: **388/388 зелёные**, оба baseline-падения исправлены
  (refresh-счётчик для response timeout, ленивый prune admission window).

Что проверено и не дало эффекта: shard ingress, `MQTTY_NONATOMIC_ID`, глобальный
packet pool, изоляция сетей, статическое распределение узлов.
Pointer-chasing 1→4 потока масштабируется на 96%, то есть память не предел.

Что осталось (M2b):
- барьер ~27% wall при 4 воркерах; инфляция работы ~1.4×;
- ~5 ГБ аллокаций на прогон (LB после lock-free ledger: ~1.6 ГБ) и 20+ gen2 GC;
- `TotalSteps` всё ещё перебирает `ThreadLocal.Values` (кэшировать на тик);
- `StaticFederator.Pump`/`IsIdle` — LINQ в горячем пути;
- возможный следующий шаг — active-node/event-driven refresh: не обновлять узлы
  с пустыми очередями и без таймеров.

**Приёмка:** инфляция работы в параллели < 20%; LB refresh ≥ 2×, далее цель 3×.
**Факт M2:** LB refresh 1.82×, Relay 2.38×, Federation 2.44×.

### M3. Распределение работы и хвост тика (P1, 2 дня)

Файл: `NetworkSimulator.cs`.

- Тяжёлые узлы (брокеры) захватывать по одному; лёгкие — чанками.
- Порядок обхода: сначала тяжёлые узлы (LPT), затем лёгкие; чанк для узлов = 1.
- При необходимости — per-node EMA стоимости и адаптивный чанк.
- Не менять порядок side-effect'ов, от которого зависят метрики; проверять G4.

**Приёмка:** `p2_parallelism` ≥ 7.2; `max/avg` загрузки воркеров < 1.3.
**Факт:** `nodeChunk=1` включён (лучший результат). Cost-aware LPT проверен и отклонён
(1.38× против 1.61×): динамический захват лучше.

### M4. Горячий путь Federation (P0, 4–5 дней) — ВЫПОЛНЕНО (частично)

Файлы: `FederationDeliveryLedger.cs`, `StaticFederator.cs`, `FederationTopology.cs`, `FederationRunner.cs`.

- **Ledger:** убрать единый `lock`; `publications` — `ConcurrentDictionary`/иммутабельный снапшот; `received`/`histograms`/`duplicates` — шардирование по receiver; `SHA-256` считать **вне** блокировки; `Receipts()` сортировать один раз на снапшоте.
- **StaticFederator:** предвычисленный массив целей (`OrderBy` один раз в конструкторе), без LINQ в `Pump`.
- **Pump:** параллелить по федераторам (у каждого свои output-клиенты, пересечений нет) либо складывать в фазу refresh.
- **IsIdle:** O(1) счётчик активной работы в `FederationTopology`; `Graph.IsQuiescent` кэшировать на тик, не сканировать на каждый вызов.
- **Drain:** один `Pump` + один `Graph.Refresh` на тик; без внутренних `while` (уже исправлено); таймаут из конфигурации, в бенчмарке — ≥ 10 с.

**Приёмка:** Federation refresh ≥ 3×; end-to-end ≥ 3.5× на 4C/8T; `status=completed`.

**Факт:** ledger переведён на lock-free структуры (ConcurrentDictionary + per-receiver
lock), SHA вне lock, `Pump` без `OrderBy`, `IsIdle` кэшируется на тик.
Federation 1.96–1.98× wall / 2.46–2.49× refresh. Параллельный `Pump` через
`Parallel.For` проверен и отклонён (1.52× против 1.98×). Остаётся последовательный
`Pump` и инфляция node-работы.

### M5. Не-refresh фазы и экспорт (P1, 2 дня)

Файлы: `LoadBalancingExperimentRunner.cs`, `FederationRunner.cs`, `FederationResultWriter.cs`, `ExperimentResultWriter.cs`.

- Параллелить `BeginConnect`/`BeginSubscribe`.
- Заменить `Parallel.For` на каждый тик в `Drive()` на последовательный цикл или постоянный пул; порог выбирать по замеру.
- Экспорт вынести за пределы гейта (`--no-export`), внутри — стриминг и одна сортировка.
- Проверить стоимость `ResetMeasurement` и construct; при необходимости кэшировать.

**Приёмка:** фиксированные расходы reference-конфига < 0.1 с.

### M6. Масштабная проверка и документация (P0, 3 дня) — ВЫПОЛНЕНО (частично)

- Прогнать S/M/L-конфиги, 5 повторов, медианы; зафиксировать результаты в этом документе.
- Обновить `AGENTS.md` (истинная картина, ограничения, команды).
- При необходимости обновить цифры в `doc/benchmark-article.md`; новых документов не создавать.
- Добавить CI-порог G1/G2 (мягкий warning на 4C/8T, жёсткий на ≥ 8 физ. ядрах).

**Факт M6 (4C/8T, 4 воркера):**

| Конфиг | wall | refresh | core | status |
|---|---:|---:|---|---|
| LB L: 64 брокера, 4 сегмента, 256 публикаторов, 20k rps, 3 с | 2.27× | 2.51× | match | completed |
| Relay L: 16 релеев, 4 сегмента, 16 клиентов, 80 повторов | 2.12× | 2.15× | match | completed |
| Fed L: 16 брокеров, 256 публикаторов, 2k rps, 1 с | 1.93× | 2.39× | match | completed |
| Fed overload: 10k rps, 3 с | 2.06× | 2.55× | MISMATCH | completed |

Масштабирование не разваливается, но целевых 3.5–4.5× на 4C/8T пока нет.
Fed-overload не годится для G4 (`queueDrops`/unique зависят от порядка refresh).

---

## 5. Протокол измерений

```bash
# baseline / gate
dotnet build src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release
dotnet run --project src/EntityFX.MqttSimulator/EntityFX.MqttY.Benchmark.Compare/EntityFX.MqttY.Benchmark.Compare.csproj -c Release --no-build -- \
  --repeat 5 --measure 5 --drain 10 --no-export --json results/scale-gate.json

# профиль фаз
MQTTY_REFRESH_STATS=1 MQTTY_WORKER_STATS=1 dotnet run ... --no-build

# кривая масштабирования
for n in 1 2 4 8; do DOTNET_PROCESSOR_COUNT=$n dotnet run ... --no-build; done
```

Правила:
- один и тот же seed, топология, нагрузка для seq и par;
- медиана из 5, отдельно min/max/CV; порядок seq/par чередовать;
- экспорт и построение топологии — вне end-to-end гейта, но выводятся отдельно;
- фиксировать модель CPU и число физических ядер в отчёте;
- метрики G4: core (counts, unique, goodput, publish p50–p99) — строго; receiver-латентность и `queueDrops` — diagnostics; overload-конфиги для G4 не использовать.

---

## 6. Конфигурации

| Конфиг | Назначение | Параметры |
|---|---|---|
| **S** (reference) | быстрый gate | 16 брокеров, 4 сегмента, 64 публикатора, 10k rps, measure 3–5 с, drain 10 с |
| **M** | масштаб | 32 брокера, 4 сегмента, 128 публикаторов, 20k rps, measure 3 с |
| **L** | стресс | 64 брокера, 4 сегмента, 256 публикаторов, 20k rps, measure 3 с |
| **Fed-L** | федерация | 16 брокеров (лимит), 256 публикаторов, 10k rps, measure 3 с, drain 10 с |

Ограничения валидаторов: LB — `BackendCount ≤ 64`, `BackendCount * NetworkLength ≤ 1024`, публикаций ≤ 1 000 000; Federation — `Brokers ≤ 16`, публикаций ≤ 100 000, `publications * receivers ≤ 1 000 000`.

---

## 7. Ожидаемый эффект по этапам

| Этап | Ожидание | На что влияет |
|---|---|---|
| M1 (факт) | +20–25% refresh | спин-барьер на физических ядрах |
| M2 (факт) | LB +35–50%, Relay +4%, Fed +10% | serial publish, node chunk=1 |
| M3 | +5–15% | хвост тика |
| M4 | Federation +1.3–2× | ledger lock, Pump, IsIdle |
| M5 | +10–20% end-to-end | фиксированные расходы |
| M6 | — | подтверждение/корректировка |

Кумулятивно: **LB/Relay 3.5–4.5×, Federation 3.5–4.5×** на 4C/8T; на 8 физических ядрах — 4–8×.

---

## 8. Риски

| Риск | Митигация |
|---|---|
| SMT: 8 логических = 4 физических | гейт по физическим ядрам; `--workers 4` vs `8` |
| Потеря детерминизма | G4 на каждом этапе; сравнение полных метрик |
| False sharing / кэш | padding счётчиков, минимум общих записей, снапшоты |
| GC / аллокации | убрать LINQ в hot path, `--no-export`, server GC |
| Шум измерений | прогрев, чередование, медиана, фиксация частот |
| Гонки в ledger/Pump | single-writer на сущность; стресс-тесты; TSan-подобные сценарии |
| Federation drain timeout | таймаут из конфига, диагностика частичных очередей |

---

## 9. Порядок и сроки

| Неделя | Работы |
|---|---|
| 1 | M0, M1 — **выполнено** |
| 2 | M2 — **выполнено частично** |
| 3 | M2b (аллокации/TotalSteps/active nodes), M3 (распределение), M4 (Federation) |
| 4 | M5 (не-refresh фазы), M6 (S/M/L, CI-порог, AGENTS/статья) |

Правило: после каждого этапа — прогон G1–G4; если этап не дал ожидаемого, он откатывается, а не «докручивается» поверх.

---

## 10. Чек-лист приёмки

- [ ] G1: refresh speedup ≥ 3.5× (4C/8T) / ≥ 4× (8 физ.)
- [ ] G2: end-to-end speedup ≥ 3.5× / ≥ 4×
- [ ] G3: efficiency(physical_cores) ≥ 50 %
- [ ] G4: метрики seq == par, `errors == 0`
- [ ] G5: масштабный конфиг ≥ 4×, `completed`
- [ ] G6: 0 новых падений тестов; полный набор 388/388 (выполнено)
- [ ] S/M/L-результаты зафиксированы в этом документе
- [ ] `AGENTS.md` отражает фактическое состояние и ограничения
