using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace EntityFX.MqttY.Federation;

public static class FederationResultWriter
{
    private static readonly Lazy<object> Runtime = new(FederationRuntimeIdentity.Read);
    public static void Claim(string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output directory: " + output);
        Directory.CreateDirectory(output);
        using var marker = new FileStream(Path.Combine(output, ".experiment"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
    public static void Json(string path, object value)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, value, FederationInput.Json);
        File.Move(temp, path, true);
    }
    public static void Seal(string output) => Json(Path.Combine(output, "sha256.json"), Directory.GetFiles(output, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFullPath(p) != Path.GetFullPath(Path.Combine(output, "sha256.json")) && Path.GetFileName(p) != ".experiment")
        .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(output, p).Replace('\\', '/'), FileSha256));
    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
    public static void Write(string output, FederationInput input, FederationSummary s, FederationPublication[] publications, DeliveryReceipt[] receipts, TransmissionResult[] transmissions, string? graphMl, TransitNetworkEvidence? networkEvidence = null,
        FederationDrainDiagnostics? drainDiagnostics = null)
    {
        var o = input.Options; var inputs = Path.Combine(output, "inputs"); Directory.CreateDirectory(inputs);
        File.WriteAllBytes(Path.Combine(inputs, "config.json"), input.Configuration);
        if (input.ProfileBytes != null) File.WriteAllBytes(Path.Combine(inputs, "profile.json"), input.ProfileBytes);
        Json(Path.Combine(output, "run-config.json"), o with { BrokerProfilePath = input.ProfileBytes == null ? null : "inputs/profile.json" });
        Json(Path.Combine(output, "summary.json"), s); Json(Path.Combine(output, "publications.json"), publications); Json(Path.Combine(output, "transmissions.json"), transmissions);
        Json(Path.Combine(output, "delivery-receipts.json"), receipts);
        if (networkEvidence != null) Json(Path.Combine(output, "network-evidence.json"), networkEvidence);
        if (drainDiagnostics != null) Json(Path.Combine(output, "drain-diagnostics.json"), drainDiagnostics);
        Json(Path.Combine(output, "phases.json"), s.Phases); Json(Path.Combine(output, "histograms.json"), new { publish = s.PublishHistogramTicks, receiver = s.ReceiverHistogramTicks, s.TickMilliseconds });
        Json(Path.Combine(output, "manifest.json"), new
        {
            schemaVersion = 1,
            experiment = "static-federation-v1",
            resolvedOptions = o,
            configurationSha256 = Convert.ToHexString(SHA256.HashData(input.Configuration)).ToLowerInvariant(),
            input.ProfileSha256,
            profileProvenance = ProfileProvenance(input.ProfileBytes),
            profileLookup = s.Sources.GroupBy(p => p.Broker).Select(g => new { broker = g.Key, publishers = g.Count(), o.PayloadBytes, o.Qos }),
            physicalEdges = FederationGraph.Edges(o),
            networkEvidence = networkEvidence == null ? null : "network-evidence.json",
            topologyVerified = graphMl != null,
            relayImplementation = "MqttRelay full mesh, source-only export, imported messages delivered locally",
            assumptions = new[] { "Static topology; no discovery, election, beacons or failover", "Separate ingress, routing and delivered topics; no No Local", "Profile lookup uses original publishers; forwarding traffic and fanout are outside calibration conditions", "TopicPayloadBytes excludes MQTT/network headers; actual model transports complete serialized packets", "Warmup is drained before measurement; all model results are exploratory, not native federation fidelity" },
            runtime = Runtime.Value
        });
        File.WriteAllLines(Path.Combine(output, "receivers.csv"), new[] { "site,uniqueDeliveries,duplicates" }.Concat(s.Receivers.Select(r => $"{r.Site},{r.UniqueDeliveries},{r.Duplicates}")), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(output, "sources.csv"), new[] { "publisher,site,broker,attempted,admitted,completed,rateRejected,publishFailed" }.Concat(s.Sources.Select(p => $"{p.Publisher},{p.Site},{p.Broker},{p.Metrics.Attempted},{p.Metrics.Admitted},{p.Metrics.Completed},{p.Metrics.RateRejected},{p.Metrics.PublishFailed}")), new UTF8Encoding(false));
        if (graphMl != null)
        {
            File.WriteAllText(Path.Combine(output, "topology.graphml"), graphMl.Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), new UTF8Encoding(false));
            FederationDiagrams.Write(output, o, graphMl);
        }
        string N(double? n) => n?.ToString("0.######", CultureInfo.InvariantCulture) ?? "notApplicable";
        var report = new StringBuilder($"# Статическая федерация MQTT\n\nРежим: **{o.Mode}**; модель: **{(o.Ideal ? "идеальная" : o.BrokerType)}**; QoS: **{o.Qos}**; seed: **{o.Seed}**. Статус: **{s.Status}**.\n\n");
        report.AppendLine("| Показатель | Значение |\n| --- | ---: |");
        foreach (var row in new (string, double?)[] { ("Запланировано", s.ScheduledPublishes), ("Попытки исходных Publish", s.ApiAttempts), ("Исходные завершения/с", s.SourceCompletedRps), ("Ожидаемые пары ID–получатель", s.ExpectedDeliveries), ("Уникальные доставки", s.UniqueDeliveries), ("Goodput доставок/с", s.UniqueGoodput), ("Недоставка среди ожидаемых пар", s.DeliveryLoss), ("Получили все subscribers", s.FullyDeliveredPublications), ("p99 ACK, мс", s.PublishP99Ms), ("p99 получения, мс", s.ReceiverP99Ms), ("Подавлено повторов", s.SuppressedDuplicates), ("Дубликаты у subscribers", s.DuplicateDeliveries), ("Попытки пересылки", s.ForwardAttempts), ("Байты тем и payload пересылок", s.ForwardTopicPayloadBytes), ("Переполнения очередей", s.QueueDrops), ("Исчерпания числа переходов", s.HopLimitDrops), ("Доставки в drain", s.UniqueDuringDrain) }) report.AppendLine($"| {row.Item1} | {N(row.Item2)} |");
        report.AppendLine("\n![Физическая сеть](topology-physical.svg)\n\n![Логические пути](topology-logical.svg)\n");
        report.AppendLine("## Формулы и ограничения\n\nRPS исходных завершений = завершения только publishers / длительность нагрузки. Goodput = число уникальных пар ID–получатель / ту же длительность. Ожидаемые пары = API-попытки × число subscribers; потери включают немедленные отказы исходного Publish. Backpressure = заданные слоты − API-попытки. Drain входит в измерительную когорту; прогрев исключён по ID. Квантили рассчитаны по объединённым гистограммам методом верхнего ранга. QoS 0 ACK latency и показатели доставки при отсутствии subscribers неприменимы.\n\nЭто статическая пересылка, не самоорганизующийся алгоритм статьи Ribas–Spohn. Федерация рассылает по соседям с подавлением повторов; Relay соединяет каждый брокер со всеми остальными; экспортирует только исходные локальные сообщения и не пересылает импорт повторно. Темы input/routing/delivered разделены. CPU/RSS, EST, выбор ядра и отказоустойчивость не моделируются. Профилированные результаты не подтверждают натурную точность при дополнительной пересылке и fanout. Короткое окно проверяет механизм, а не стационарный p99.\n");
        if (s.Error != null) report.AppendLine("## Причина отказа\n\n```text\n" + s.Error + "\n```\n");
        report.AppendLine("Повтор: `run --config run-config.json --output <новый-каталог>`. Входы сохранены в inputs; summary.json не содержит сведений о компьютере. Хэши подтверждают целостность, проверяемость требует пересчёта и повторного запуска. SVG/Mermaid построены после сверки площадок, связей и расположения клиентов с GraphML.");
        File.WriteAllText(Path.Combine(output, "report.md"), report.ToString(), new UTF8Encoding(false)); Seal(output);
    }
    private static JsonElement? ProfileProvenance(byte[]? bytes)
    {
        if (bytes == null) return null;
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.GetProperty("provenance").Clone();
    }
}
