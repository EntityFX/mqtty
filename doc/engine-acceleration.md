# Ускорение движка MqttY: было → стало (примеры кода)

Документ фиксирует структурные изменения движка, которые дали ускорение,
с фрагментами кода до и после. Все изменения проверены golden-trace тестами
и полным набором интеграционных тестов (445/445); модельные метрики
(`Ticks`, `Steps`, `Errors`, доставки) не меняются.

Стенд: Xeon E5-2680 v3, 12 физических / 24 логических ядра, .NET 6,
`workers=12`, `--no-export`, медианы ≥ 5 прогонов. Подробный протокол и
таблицы результатов — [performance.md](performance.md).

## 1. Фазный барьер: counter + `Thread.Yield` → dissemination

Два fence на тик — самая дорогая точка синхронизации. Старый барьер имел один
общий счётчик и общую линию generation: каждый `Decrement` инвалидировал линию,
на которой крутились остальные воркеры, а после `spinLimit` ожидание уходило в
`Thread.Yield()` — системный вызов на каждом тике.

**Было** (`TickWorkerPool.cs`, коммит `5e1f727`):

```csharp
private sealed class SpinBarrier : IPhaseBarrier
{
    private int _remaining;
    private int _generation;

    public void SignalAndWait()
    {
        var generation = Volatile.Read(ref _generation);
        if (Interlocked.Decrement(ref _remaining) == 0)
        {
            Volatile.Write(ref _remaining, _participants);
            Volatile.Write(ref _generation, generation + 1);
            return;
        }

        var spin = 0;
        while (Volatile.Read(ref _generation) == generation)
        {
            if (spin++ < _spinLimit) Thread.SpinWait(32);
            else Thread.Yield();          // syscall на каждом ожидании
        }
    }
}
```

Изолированный микробенчмарк: BCL `Barrier` на 12 участниках — **~188 µs**
на барьер, старый counter — единицы микросекунд, но с `Yield`.

**Стало** (dissemination, log₂N раундов, отдельная cache-line на слот):

```csharp
public void SignalAndWait(int worker)
{
    var sequence = ++_sequence[worker * Stride];

    for (var round = 0; round < _rounds; round++)
    {
        var cell = (worker * _rounds + round) * Stride;
        Volatile.Write(ref _tickets[cell], sequence);

        var partner = (worker + (1 << round)) % _participants;
        var partnerCell = (partner * _rounds + round) * Stride;

        var spin = 0;
        while (Volatile.Read(ref _tickets[partnerCell]) < sequence)
        {
            if (spin++ < _spinLimit) Thread.SpinWait(32);
            else Thread.Yield();
        }
    }
}
```

Каждая ячейка `(worker, round)` живёт на своей 64-байтной линии: один writer,
один reader, без ложного разделения. В изоляции 12 участников — **~2.2 µs**
на барьер вместо сотен микросекунд; в симуляции refresh вырос с 6.19× до
**9.10×**, wall — с 5.23× до **7.14×**, а CPU прогона упал с 23.1 до 16.2 с
(раньше спин сжигался в `Yield`).

## 2. Публикация тика: `lock` → release/acquire

Старый воркер после пробуждения заново брал монитор, чтобы скопировать
описание работы. При 12 воркерах это lock convoy на каждом тике.

**Было:**

```csharp
lock (_gate)
{
    if (_stop) return;
    lastGeneration = _generation;
    phase1 = _phase1;
    phase2 = _phase2;
    count1 = _count1;
    count2 = _count2;
    twoPhase = _twoPhase;
}
```

**Стало:** поля публикуются под `lock` в `Run`, а воркер читает их после
acquire-чтения поколения — монитор нужен только на парковке:

```csharp
if (!WaitForNextGeneration(lastGeneration)) return;
if (_stop) return;

// acquire-pair с Volatile.Write в Run
lastGeneration = Volatile.Read(ref _generation.Value);
var phase1 = _phase1;
var phase2 = _phase2;
var count1 = _count1;
var count2 = _count2;
var twoPhase = _twoPhase;
```

Безопасно, потому что вызывающий не может опубликовать следующий тик, пока
все участники не прошли финальный fence предыдущего.

## 3. Раздача узлов: общий atomic index → owned ranges

Один `Interlocked.Increment` на узел при 12 воркерах сериализует выдачу и
заставляет все ядра бить по одной cache-line.

**Было:**

```csharp
private void RunPhaseChunked(Action<int>? body, int count, ref int index, int chunkSize)
{
    while (true)
    {
        var start = Interlocked.Add(ref index, chunkSize) - chunkSize;
        if (start >= count) return;
        ...
    }
}
```

**Стало:** у каждого воркера своя полоса и свой padded-счётчик; воркер
исчерпывает свою полосу, затем переходит к следующей:

```csharp
private void RunOwnedPhase(Action<int>? body, int count, PaddedCounter[] heads,
    int chunkSize, int worker)
{
    for (var offset = 0; offset < _workerCount; offset++)
    {
        var victim = (worker + offset) % _workerCount;
        var limit = (int)((long)count * (victim + 1) / _workerCount);
        ref var next = ref heads[victim].Value;

        while (Volatile.Read(ref next) < limit)
        {
            var start = Interlocked.Add(ref next, chunkSize) - chunkSize;
            var end = Math.Min(start + chunkSize, limit);
            for (var item = start; item < end; item++) body(item);
        }
    }
}

// dense-списки: порог 8 × workers
private bool UseOwnedRanges(int count) => count >= _workerCount * 8;
```

Эффект: **−26 %** wall на крупных списках (2.11 → 1.55 с). На малых
топологиях остаётся глобальный индекс — там статическая нарезка хуже
балансирует.

## 4. Padding: общие поля → отдельные cache-line

**Было:** `int _index1, _index2, _generation;` — все на одной линии.

**Стало:**

```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedCounter
{
    [FieldOffset(0)] public int Value;
}
```

Барьер на `nodeChunk=2`: **1.64 с** против 1.85–2.85 с без padding.

## 5. Описания enum: рефлексия на клиента → memoize

Каждый клиент и брокер строит группу счётчиков, а та запрашивает описание и
категорию для каждого значения `MqttPacketType`. `Enum.GetMember` не кэшируется
рантаймом: ~600 клиентов × ~150 вызовов = ~100 тыс. рефлексивных запросов на
построение топологии.

**Было:**

```csharp
public static string GetEnumDescription(this Enum e)
{
    var descriptionAttribute = e.GetType().GetMember(e.ToString())[0]
        .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)[0]
        as DescriptionAttribute;
    return descriptionAttribute?.Description ?? string.Empty;
}
```

**Стало:**

```csharp
private static readonly ConcurrentDictionary<(Type Type, string Name), string> Descriptions = new();

public static string GetEnumDescription(this Enum e)
{
    var key = (e.GetType(), e.ToString());
    if (Descriptions.TryGetValue(key, out var cached)) return cached;

    var description = (e.GetType().GetMember(key.Item2)[0]
        .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)[0]
        as DescriptionAttribute)?.Description ?? string.Empty;

    Descriptions[key] = description;
    return description;
}
```

Эффект: `construct` **0.22 → 0.05 с** (−77 %).

## 6. Словари топологии: immutable на каждое чтение → кэш по версии

**Было:**

```csharp
public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();
public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();
```

Каждое обращение в publish/refresh пересобирало immutable-словарь.

**Стало:**

```csharp
private IReadOnlyDictionary<string, IClient>? _clientsCache;
private int _clientsCacheVersion = -1;

public IReadOnlyDictionary<string, IClient> Clients
{
    get
    {
        if (_clientsCache == null || _clientsCacheVersion != _nodesVersion)
        {
            _clientsCache = _clients.ToImmutableDictionary();
            _clientsCacheVersion = _nodesVersion;
        }
        return _clientsCache;
    }
}
```

Версия инкрементируется в `AddClient/RemoveClient/AddServer/...`; `UnlinkAll`
и `Link/Unlink` также двигают версию. Эффект: −4 % wall и меньше аллокаций.

## 7. Порядок узлов: по адресу → LPT (тяжёлые первыми)

Финальный fence ждёт самый медленный узел. Брокеры и приложения дороже
клиентов, поэтому их выгодно обрабатывать первыми.

**Было:**

```csharp
_orderedNodes = snapshot.Nodes;
```

**Стало:**

```csharp
_orderedNodes = snapshot.Nodes
    .OrderBy(node => NodePriority(node.Key.NodeType))   // Server → Application → Client
    .ThenBy(node => node.Key.Address, StringComparer.Ordinal)
    .ToArray();
```

Эффект на `original`: `p2_parallelism` **6.46 → 7.3**, суммарное время в
барьере −12 %. Детерминизм подтверждён golden-trace.

## 8. Спин между тиками: 64 → 4096

Тик длится десятки микросекунд; при 64 итерациях воркеры паркуются почти
каждый тик и платят пробуждение.

**Было:** `SpinIterations = 64`, `BarrierSpinIterations = 64`.

**Стало** (`ApplyParallelDefaults`):

```csharp
if (Environment.GetEnvironmentVariable("MQTTY_SPIN_ITERATIONS") == null)
    Environment.SetEnvironmentVariable("MQTTY_SPIN_ITERATIONS", "4096");
if (Environment.GetEnvironmentVariable("MQTTY_BARRIER_SPIN") == null)
    Environment.SetEnvironmentVariable("MQTTY_BARRIER_SPIN", "2048");
```

`original --sequential`: 2.52 → 2.31–2.39 с (+6–8 %).

## 9. Путь пакета: `.ToArray()` → без копии

**Было:**

```csharp
var pathToRemote = NetworkSimulator.PathFinder.GetPath(fromNetwork, toNetwork).ToArray();
```

Path finder уже возвращает закэшированный массив, но каждый remote-пакет
копировал его.

**Стало:**

```csharp
var path = NetworkSimulator.PathFinder.GetPath(fromNetwork, toNetwork);
var pathToRemote = path as INetwork[] ?? path.ToArray();
```

## 10. Sweep-шарды: round-robin → LPT

**Было:**

```csharp
public bool Contains(int ordinal) => Count <= 1 || ordinal % Count == Index;
```

Тяжёлые конфигурации могли попасть в один шард, остальные простаивали в конце.

**Стало:** LPT-назначение по оценке стоимости, ординалы передаются набором:

```csharp
var order = Enumerable.Range(0, total)
    .OrderByDescending(i => weights[i])
    .ThenBy(i => i)
    .ToArray();

foreach (var task in order)
{
    var target = 0;
    for (var s = 1; s < shards; s++)
        if (loads[s] < loads[target]) target = s;
    assignments[target].Add(task);
    loads[target] += weights[task];
}
```

Вес: `brokers × nets × clients × (repeats + 1) × (strategy == 0 ? 1 : 2)`.

## 11. `original`: три прогона подряд → три процесса

**Было:** `seq`, `par`, `par+counters` выполнялись один за другим в одном
процессе (нельзя параллельно: `CounterClock` — статик на процесс).

**Стало:** родитель запускает три дочерних процесса; `seq` работает на
вызывающем потоке, два параллельных варианта делят физические ядра
(`workers = physical / 2`), а результаты собираются из строк `MQTTY_ROW`:

```csharp
var workersForRun = run == "seq" ? 1 : Math.Max(1, physical / 2);
startInfo.Environment["MQTTY_WORKERS"] = workersForRun.ToString(...);

startInfo.ArgumentList.Add("original");
startInfo.ArgumentList.Add("--run"); startInfo.ArgumentList.Add(run);
startInfo.ArgumentList.Add("--output"); startInfo.ArgumentList.Add(dir);
```

Эффект: 2.60 → **1.74 с** (1.49×), метрики моделей идентичны.

## 12. Отклонённые эксперименты (с кодом)

**Пиннинг потоков через `pthread_setaffinity_np`** — каждый воркер на своём
CPU. Результат: `par wall 1.7 → 80 с`. Откатано полностью.

**Completion-counter вместо второго fence** — воркеры сигналят и уходят в
generation-wait, ждёт только вызывающий:

```csharp
if (caller)
{
    var spin = new SpinWait();
    while (Volatile.Read(ref _completedPhase2.Value) < _threads.Length) spin.SpinOnce();
    Volatile.Write(ref _completedPhase2.Value, 0);
}
else
{
    Interlocked.Increment(ref _completedPhase2.Value);
}
```

Результат: `par wall 1.6 → 13.7 с`. Откатано.

**Кэш quiescence-состояния узла** (`_hasActivity`): регрессия 1.49 → 1.63 с.

**Network fast-path** (пропуск `_admission.Prune` у пустых сетей): регрессия,
проверки дороже выигрыша.

**`MQTTY_PARALLEL_PUBLISH=1`**: 16 брокеров — 1.53 → 2.1 с; 64 брокера — без
выигрыша.

## Итоговые результаты

| Конфиг | wall было | wall стало | refresh было | refresh стало |
|---|---:|---:|---:|---:|
| Relay | 5.23× | **7.62×** | 6.19× | **8.89×** |
| LB L | 4.76× | **5.17×** | 5.72× | **5.87×** |
| Federation L | 3.95× | **4.79×** | 4.96× | **5.70×** |

Матрица 8 конфигураций: 34.0 с → 19.7 с (**1.73×**), `core=match`.

Сравнение с августом (`9655fe8`) на большой сети (16/1/10/20):
`seq` 2.76 → 2.11 с, `par` 1.98 → **0.43 с**, `par+counters` 8.54 → **0.41 с**,
внутренний `par/seq` 1.39× → **4.88×**; при этом текущая модель выполняет в
3.1 раза больше шагов.

## Ссылки

- [performance.md](performance.md) — протокол, фазовые профили, отклонённые эксперименты, методика сравнения с Aedes.
- [analysis/message-scaling/article.md](analysis/message-scaling/article.md) — статья с результатами.
- [analysis/message-scaling/super-article.md](analysis/message-scaling/super-article.md) — сводное исследование.
