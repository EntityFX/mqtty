using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EntityFX.MqttY.LoadBalancing;

public static class ExperimentResultWriter
{
    private static readonly Lazy<object> RuntimeIdentity = new(ReadRuntimeIdentity);
    public static void CreateOutput(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose a new output directory: " + directory);
        Directory.CreateDirectory(directory);
        // CreateNew protects the ownership marker from being overwritten by a competing invocation.
        using var marker = new FileStream(Path.Combine(directory, ".experiment"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public static void Write(string directory, PreparedExperiment input, ExperimentSummary summary, string? graphMl, string? plantUml)
    {
        var options = input.Options;
        var inputs = Path.Combine(directory, "inputs");
        Directory.CreateDirectory(inputs);
        File.WriteAllBytes(Path.Combine(inputs, "config.json"), input.Configuration.Bytes);
        WriteJson(Path.Combine(directory, "run-config.json"), options with
        {
            BrokerProfiles = null, BrokerProfilePath = input.Profile == null ? null : "inputs/profile.json"
        });
        if (input.Profile != null) File.WriteAllBytes(Path.Combine(inputs, "profile.json"), input.Profile.Source.Bytes);
        if (input.CalibrationObservations != null) File.WriteAllBytes(Path.Combine(inputs, "calibration-observations.json"), input.CalibrationObservations.Bytes);
        WriteJson(Path.Combine(directory, "manifest.json"), new
        {
            schemaVersion = 1, experiment = "connection-balancing-v1", transportTiming = "fixed",
            loadScheduling = "equal per-publisher slots; closed-loop QoS1/2; bounded per-publisher credit, no reassignment of blocked load",
            scope = options.Ideal ? "ideal mechanism check" : "exploratory calibrated connection-balancing model; not native LB fidelity",
            assumptions = new[] { "No TCP proxy overhead, health checks or session migration", "One direct receiver per independent backend",
                "No inter-backend subscription sharing", "CPU/RSS and native proxy resources are not modeled" },
            resolvedOptions = options, configuration = new { input.Configuration.Path, input.Configuration.Sha256 },
            profile = input.Profile == null ? null : new { input.Profile.Source.Path, input.Profile.Source.Sha256, input.Profile.Provenance },
            calibrationObservations = input.CalibrationObservations == null ? null : new { input.CalibrationObservations.Path, input.CalibrationObservations.Sha256 },
            backendProfileLookup = Enumerable.Range(0, options.BackendCount).Select(backend => new
                { backend, options.BrokerType, options.PayloadBytes, options.PublishQos, publishers = input.Assignments.Count(n => n == backend),
                    configuredOfferedRps = options.OfferedRps * input.Assignments.Count(n => n == backend) / options.TotalPublishers }),
            assignments = input.Assignments, runtime = RuntimeIdentity.Value
        });
        WriteJson(Path.Combine(directory, "summary.json"), summary);
        WriteJson(Path.Combine(directory, "histograms.json"), new { publishTicks = summary.PublishLatencyHistogramTicks,
            receiverTicks = summary.Delivery.LatencyHistogramTicks, summary.TickMilliseconds });
        WriteJson(Path.Combine(directory, "phases.json"), summary.Phases);
        File.WriteAllLines(Path.Combine(directory, "assignments.csv"), new[] { "publisher,backend" }
            .Concat(input.Assignments.Select((backend, publisher) => $"{publisher},{backend}")), new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(directory, "backend-metrics.csv"), new[]
        {
            "backend,publishers,attempted,admitted,completed,completedBeforeDrain,completedDuringDrain,expectedDeliveries,delivered,rateRejected,publishFailed,deliveryDropped,completedRps,uniqueReceived,uniqueReceivedBeforeDrain,uniqueReceivedDuringDrain,uniqueGoodput"
        }.Concat(summary.Backends.Select(b => string.Join(",", new object[]
        {
            b.Backend, b.Publishers, b.Metrics.Attempted, b.Metrics.Admitted, b.Metrics.Completed,
            b.CompletedBeforeDrain, b.Metrics.Completed-b.CompletedBeforeDrain, b.Metrics.ExpectedDeliveries,
            b.Metrics.Delivered, b.Metrics.RateRejected, b.Metrics.PublishFailed, b.Metrics.DeliveryDropped,
            Number(b.Metrics.Completed/summary.MeasurementSeconds), b.UniqueReceived, b.UniqueReceivedBeforeDrain,
            b.UniqueReceived-b.UniqueReceivedBeforeDrain, Number(b.UniqueReceived/summary.MeasurementSeconds)
        }))), new UTF8Encoding(false));
        if (graphMl != null) File.WriteAllText(Path.Combine(directory, "topology.graphml"), graphMl.Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), new UTF8Encoding(false));
        if (plantUml != null) File.WriteAllText(Path.Combine(directory, "topology.puml"), plantUml, new UTF8Encoding(false));
        var report = new StringBuilder("# Балансировка подключений MQTT\n\n");
        var brokerLabel = options.Ideal ? "идеальная модель без реального профиля" : options.BrokerType;
        report.AppendLine($"Брокер: **{brokerLabel}**; режим: **{options.Mode}**; QoS: **{options.PublishQos}**; seed: **{options.Seed}**. Статус: **{summary.Status}**.\n");
        report.AppendLine("| Показатель | Значение |\n| --- | ---: |");
        foreach (var (label, value) in new[]
        {
            ("Общая заданная нагрузка, сообщений/с", (double?)summary.ConfiguredOfferedRps),
            ("Запланировано публикаций", (double?)summary.ScheduledPublishes), ("Вызовы Publish", (double?)summary.ApiAttempts),
            ("Немедленные отказы Publish", (double?)summary.SendRejected), ("Не отправлено из-за ограничения подачи", (double?)summary.BackpressureUnsent),
            ("Завершения backend / длительность нагрузки, сообщений/с", summary.BackendCompletedRps),
            ("Уникально получено / длительность нагрузки, сообщений/с", summary.UniqueGoodput),
            ("Недоставка среди API-попыток, доля", summary.AttemptDeliveryLoss),
            ("Ошибки backend / попытки backend, доля", summary.BackendPublishFailureRate),
            ("Недоставка backend / ожидаемые доставки, доля", summary.BackendDeliveryLossRate),
            ("p99 завершения публикации, мс", summary.PublishP99Ms), ("p99 первого получения, мс", summary.ReceiverP99Ms),
            ("Уникальные получения в drain", (double?)summary.UniqueReceivedDuringDrain)
        }) report.AppendLine($"| {label} | {Number(value)} |");
        report.AppendLine("\n## Границы вывода\n");
        report.AppendLine("Изучается назначение постоянных соединений независимым брокерам. Receivers подключены к своим backend напрямую. Модель не объединяет подписки; не моделирует стоимость proxy, CPU/RSS, миграцию и отказоустойчивость. Транспорт фиксирован и одинаков для сравниваемых режимов. Короткий опыт не подтверждает стационарный p99 и точность настоящего балансировщика.");
        report.AppendLine("\nGoodput и завершения включают drain измерительной когорты и делятся на длительность подачи нагрузки. Нагрузка прогрева исключена по ID/поколению. SendRejected — немедленный отказ метода, а не все протокольные ошибки. Завершения backend не сопоставляются с отдельными ID. null/notApplicable означает отсутствие применимой оценки; для QoS 0 ACK latency неприменима. Глобальные квантили вычислены по объединённым гистограммам методом ближайшего верхнего ранга.");
        if (summary.Error != null) report.AppendLine("\n## Причина технического отказа\n\n```text\n" + summary.Error + "\n```");
        report.AppendLine("\nЧисла сохранены в summary.json, backend-metrics.csv, assignments.csv и histograms.json. Точные входные байты — в inputs; происхождение — в manifest.json. SHA-256 подтверждает целостность, а проверка вычислений требует повторного запуска с этими входами и сопоставления summary.json.");
        File.WriteAllText(Path.Combine(directory, "report.md"), report.ToString(), new UTF8Encoding(false));
        Seal(directory);
    }

    public static void WriteJson(string path, object value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, ExperimentInput.Json), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
    public static void Seal(string directory) => WriteJson(Path.Combine(directory, "sha256.json"), Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
        .Where(p => Path.GetFullPath(p) != Path.GetFullPath(Path.Combine(directory, "sha256.json")) && Path.GetFileName(p) != ".experiment")
        .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(directory, p).Replace('\\', '/'),
            p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()));
    private static string Number(double? value) => value?.ToString("G17", CultureInfo.InvariantCulture) ?? "notApplicable";

    private static object ReadRuntimeIdentity()
    {
        // Metadata is outside summary.json so scientific results can be compared byte for byte.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, ".git")) && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        string? Git(string argument)
        {
            if (directory == null) return null;
            try
            {
                var info = new ProcessStartInfo("git") { WorkingDirectory = directory.FullName,
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in argument.Split(' ')) info.ArgumentList.Add(arg);
                using var process = Process.Start(info)!;
                var output = process.StandardOutput.ReadToEnd();
                return process.WaitForExit(5000) && process.ExitCode == 0 ? output.Trim() : null;
            }
            catch { return null; }
        }
        return new { framework = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(), mqttYCommit = Git("rev-parse HEAD"),
            sourceStatus = Git("status --porcelain"),
            binarySha256 = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").OrderBy(p => p, StringComparer.Ordinal)
                .ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()) };
    }
}
