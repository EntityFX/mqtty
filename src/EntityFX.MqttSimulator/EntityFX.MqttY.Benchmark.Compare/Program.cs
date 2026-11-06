using System.Diagnostics;
using System.Text.Json;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

// =====================================================================
// Сравнение последовательного (parallel=false) и параллельного (parallel=true)
// режима выполнения симуляции для трёх движков: LoadBalancing, Relay, Federation.
// Параллельность управляет обновлением графа симуляции (graph.Refresh / RefreshWithCounters).
// =====================================================================

var sw = Stopwatch.StartNew();

// Общие параметры топологии (одинаковые для всех трёх движков).
// Federation ограничивает Brokers <= 16, поэтому берём 16 узлов везде.
const int nodes = 16;          // брокеры / релеи
const int netsPerNode = 4;     // сегментов сети на узел (16*4=64 <= 1024)
const int publishers = 64;     // публикующих клиентов
const double offeredRps = 10000;
const double measurementSeconds = 0.5;
const double warmupSeconds = 0.01;
const double drainSeconds = 1;
const int seed = 42;
var tickPeriod = TimeSpan.FromTicks(1000); // 0.1 мс

var tmp = Path.Combine(Path.GetTempPath(), $"mqtty-bench-{Guid.NewGuid():N}");
Directory.CreateDirectory(tmp);
Console.WriteLine($"Временная папка: {tmp}");
Console.WriteLine($"Узлов: {nodes}, сегментов сети: {netsPerNode}, публикующих: {publishers}");
Console.WriteLine($"Нагрузка: {offeredRps:n0} р/с, измерение: {measurementSeconds} с, прогрев: {warmupSeconds} с");
Console.WriteLine($"Бюджет публикаций: {offeredRps * (warmupSeconds + measurementSeconds):n0} (лимит 1 000 000)");
Console.WriteLine($"Сегментов в сети: {nodes * netsPerNode} (лимит 1 024)");
Console.WriteLine(new string('=', 70));

// =====================================================================
// 1. LOAD BALANCING
// =====================================================================
Console.WriteLine("--- LOAD BALANCING ---");

var lbOptions = new LoadBalancingOptions
{
    Ideal = true,
    Mode = BalancingMode.RoundRobin,
    BrokerType = "Aedes",
    BackendCount = nodes,
    TotalPublishers = publishers,
    PayloadBytes = 16,
    PublishQos = 1,
    SubscribeQos = 1,
    OfferedRps = offeredRps,
    Seed = seed,
    WarmupSeconds = warmupSeconds,
    MeasurementSeconds = measurementSeconds,
    DrainTimeoutSeconds = drainSeconds,
    TickPeriod = tickPeriod,
    NetworkLength = netsPerNode,
    Network = new NetworkSettings
    {
        CapacityBytesPerSecond = 100_000_000,
        ThroughputWindowTicks = 100,
        QueueCapacity = 100_000,
        TransferTicks = 1,
    }
};
lbOptions.Validate();

var lbConfig = new InputSnapshot("generated-from-benchmark",
    JsonSerializer.SerializeToUtf8Bytes(lbOptions, ExperimentInput.Json));
var lbInput = ExperimentInput.Prepare(lbOptions, lbConfig);
var lbRunner = new LoadBalancingExperimentRunner();

(string Status, double Wall, double MeasureWall, double RefreshWall, long Errors) RunLoadBalancing(bool parallel, string dir)
{
    var s = Stopwatch.StartNew();
    var (summary, timing) = lbRunner.RunMeasured(lbInput, dir, parallel: parallel, refreshStrategy: 0);
    s.Stop();
    return (summary.Status, s.Elapsed.TotalSeconds,
        timing.PhaseWallSeconds.GetValueOrDefault("measure"),
        timing.RefreshWallSeconds,
        summary.GraphErrors);
}

var (lbSeqStatus, lbSeqWall, lbSeqMeasure, lbSeqRefresh, lbSeqErrors) = RunLoadBalancing(false, Path.Combine(tmp, "lb-sequential"));
var (lbParStatus, lbParWall, lbParMeasure, lbParRefresh, lbParErrors) = RunLoadBalancing(true, Path.Combine(tmp, "lb-parallel"));

void PrintLb(string label, double wall, double measure, double refresh, long errors, string status)
{
    Console.WriteLine($"  {label,-14} wall={wall:F4} с  measure={measure:F4} с  refresh={refresh:F4} с  errors={errors}  status={status}");
}
PrintLb("sequential:", lbSeqWall, lbSeqMeasure, lbSeqRefresh, lbSeqErrors, lbSeqStatus);
PrintLb("parallel:   ", lbParWall, lbParMeasure, lbParRefresh, lbParErrors, lbParStatus);
var lbSpeedup = lbSeqWall / lbParWall;
Console.WriteLine($"  Прирост: {lbSpeedup:F2}x  (параллель быстрее на {(1 - lbParWall / lbSeqWall) * 100:F1}%)");
Console.WriteLine();
// =====================================================================
// 2. RELAY
// =====================================================================
Console.WriteLine("--- RELAY ---");

// clients = 4 на брокер => 16*4 = 64 клиента всего; sendRepeats подобрано ~5120 сообщений
const int relayClients = 4;
const int relaySendRepeats = 80;
var relayApp = new MqttRelayApp(null, null);

(string Status, double Wall, long Ticks, long Steps, long Errors) RunRelay(bool isParallel, string label)
{
    var s = Stopwatch.StartNew();
    var graph = relayApp.ExecuteSimulation(isParallel, nodes, netsPerNode, relayClients,
        relaySendRepeats, enableCounters: false, strategy: 0, bytesTelemetry: null);
    s.Stop();
    return ("completed", s.Elapsed.TotalSeconds, graph.TotalTicks, graph.TotalSteps, graph.Errors);
}

var (relaySeqStatus, relaySeqWall, relaySeqTicks, relaySeqSteps, relaySeqErrors) = RunRelay(false, "sequential");
var (relayParStatus, relayParWall, relayParTicks, relayParSteps, relayParErrors) = RunRelay(true, "parallel");

void PrintRelay(string label, double wall, long ticks, long steps, long errors, string status)
{
    Console.WriteLine($"  {label,-14} wall={wall:F4} с  ticks={ticks:n0}  steps={steps:n0}  errors={errors}  status={status}");
}
PrintRelay("sequential:", relaySeqWall, relaySeqTicks, relaySeqSteps, relaySeqErrors, relaySeqStatus);
PrintRelay("parallel:   ", relayParWall, relayParTicks, relayParSteps, relayParErrors, relayParStatus);
var relaySpeedup = relaySeqWall / relayParWall;
Console.WriteLine($"  Прирост: {relaySpeedup:F2}x  (параллель быстрее на {(1 - relayParWall / relaySeqWall) * 100:F1}%)");
Console.WriteLine();

// =====================================================================
// 3. FEDERATION
// =====================================================================
Console.WriteLine("--- FEDERATION ---");

var fedOptions = new FederationOptions
{
    Mode = FederationMode.Federation,
    Brokers = nodes,
    Topology = "star",
    Publishers = publishers,
    PayloadBytes = 16,
    Qos = 1,
    OfferedRps = 5000,
    Seed = seed,
    WarmupSeconds = warmupSeconds,
    MeasurementSeconds = measurementSeconds,
    DrainTimeoutSeconds = drainSeconds,
    TickPeriod = tickPeriod,
    Ideal = true,
    Network = new FederationNetworkOptions
    {
        CapacityBytesPerSecond = 100_000_000,
        ThroughputWindowTicks = 100,
        QueueCapacity = 100_000,
        TransferTicks = 1,
    }
};
fedOptions.Validate();

var fedInput = FederationInput.Prepare(fedOptions);
var fedRunner = new FederationRunner();

(string Status, double Wall, double Goodput, double P99, long Errors) RunFederation(bool parallel, string dir)
{
    var s = Stopwatch.StartNew();
    var summary = fedRunner.Run(fedInput, dir, diagnostics: false,
        parallel: parallel, refreshStrategy: 1);
    s.Stop();
    return (summary.Status, s.Elapsed.TotalSeconds,
        summary.UniqueGoodput.GetValueOrDefault(),
        summary.PublishP99Ms.GetValueOrDefault(),
        summary.ForwardRejected + summary.QueueDrops);
}

var (fedSeqStatus, fedSeqWall, fedSeqGput, fedSeqP99, fedSeqErrors) = RunFederation(false, Path.Combine(tmp, "fed-sequential"));
var (fedParStatus, fedParWall, fedParGput, fedParP99, fedParErrors) = RunFederation(true, Path.Combine(tmp, "fed-parallel"));

void PrintFed(string label, double wall, double gput, double p99, long errors, string status)
{
    Console.WriteLine($"  {label,-14} wall={wall:F4} с  goodput={gput:F0} р/с  p99={p99:F2} мс  errors={errors}  status={status}");
}
PrintFed("sequential:", fedSeqWall, fedSeqGput, fedSeqP99, fedSeqErrors, fedSeqStatus);
PrintFed("parallel:   ", fedParWall, fedParGput, fedParP99, fedParErrors, fedParStatus);
var fedSpeedup = fedSeqWall / fedParWall;
Console.WriteLine($"  Прирост: {fedSpeedup:F2}x  (параллель быстрее на {(1 - fedParWall / fedSeqWall) * 100:F1}%)");
Console.WriteLine();

// =====================================================================
// ИТОГОВАЯ ТАБЛИЦА
// =====================================================================
sw.Stop();
Console.WriteLine("============================================================");
Console.WriteLine("ИТОГОВОЕ СРАВНЕНИЕ (wall-clock, seq vs par)");
Console.WriteLine("============================================================");
Console.WriteLine($"{"Движок",-18} {"Последов.",-12} {"Параллель",-12} {"Прирост",-10} {"Быстрее на",-12} {"Статус",-16}");
Console.WriteLine(new string('-', 80));
Console.WriteLine($"{"LoadBalancing",-18} {lbSeqWall,10:F4} с {lbParWall,10:F4} с {lbSpeedup,8:F2}x {(1 - lbParWall / lbSeqWall) * 100,10:F1}%   {lbParStatus,-16}");
Console.WriteLine($"{"Relay",-18} {relaySeqWall,10:F4} с {relayParWall,10:F4} с {relaySpeedup,8:F2}x {(1 - relayParWall / relaySeqWall) * 100,10:F1}%   {relayParStatus,-16}");
Console.WriteLine($"{"Federation",-18} {fedSeqWall,10:F4} с {fedParWall,10:F4} с {fedSpeedup,8:F2}x {(1 - fedParWall / fedSeqWall) * 100,10:F1}%   {fedParStatus,-16}");
Console.WriteLine();
Console.WriteLine($"Общее время бенчмарка: {sw.Elapsed.TotalSeconds:F2} с");
Console.WriteLine($"Результаты сохранены в: {tmp}");