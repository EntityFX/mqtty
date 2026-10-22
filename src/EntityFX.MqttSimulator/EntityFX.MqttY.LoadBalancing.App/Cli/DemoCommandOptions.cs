using System.Globalization;
using System.Text.Json;

namespace EntityFX.MqttY.LoadBalancing;

/// <summary>Constructs a short ideal experiment entirely from CLI arguments.</summary>
public sealed record DemoCommandOptions(LoadBalancingOptions Experiment, string? OutputDirectory)
{
    public const string HelpText =
        "demo [--brokers N] [--mode roundRobin|leastConnections|directBalanced|single]\n" +
        "     [--publishers N] [--qos 0|1|2] [--rps R] [--seconds S] [--seed N]\n" +
        "     [--output <new-directory>]\n\n" +
        "Идеальные модельные брокеры: конфиг и реальный профиль не нужны.\n" +
        "По умолчанию: 4 брокера, roundRobin, 64 publishers, QoS 1, 1000 сообщений/с,\n" +
        "0.1 модельной секунды нагрузки, seed 42. single выбирает 1 брокер.\n" +
        "--seconds: от 0 до 10 (не включая 0), шаг 0.0001 с; дробные числа через точку.\n" +
        "Без --output результаты выводятся в консоль без записи файлов.\n" +
        "--output сохраняет полный пакет результатов и конфиг для повторения.";

    public static DemoCommandOptions Parse(string[] args)
    {
        var allowed = new HashSet<string> { "--brokers", "--mode", "--publishers", "--qos", "--rps", "--seconds", "--seed", "--output" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            var name = args[i];
            if (!allowed.Contains(name)) throw new ArgumentException("Неизвестный параметр demo: " + name);
            if (i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Не указано значение " + name);
            if (!values.TryAdd(name, args[i + 1])) throw new ArgumentException("Параметр указан повторно: " + name);
        }

        int Integer(string name, int fallback) => !values.TryGetValue(name, out var value) ? fallback :
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result :
            throw new ArgumentException("Ожидается целое число для " + name);
        double Number(string name, double fallback) => !values.TryGetValue(name, out var value) ? fallback :
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result :
            throw new ArgumentException("Ожидается число с точкой для " + name);

        var mode = BalancingMode.RoundRobin;
        if (values.TryGetValue("--mode", out var modeText))
        {
            var modes = Enum.GetValues<BalancingMode>().ToDictionary(m => m.ToString(), StringComparer.OrdinalIgnoreCase);
            if (!modes.TryGetValue(modeText, out mode)) throw new ArgumentException("Неизвестный режим: " + modeText);
        }
        var seconds = Number("--seconds", .1);
        if (!(seconds > 0 && seconds <= 10)) throw new ArgumentException("demo --seconds должен быть больше 0 и не больше 10.");
        var options = new LoadBalancingOptions
        {
            Ideal = true, Mode = mode, BackendCount = Integer("--brokers", mode == BalancingMode.Single ? 1 : 4),
            TotalPublishers = Integer("--publishers", 64), PublishQos = Integer("--qos", 1),
            SubscribeQos = Integer("--qos", 1), OfferedRps = Number("--rps", 1000), Seed = Integer("--seed", 42),
            WarmupSeconds = .01, MeasurementSeconds = seconds, DrainTimeoutSeconds = 1
        };
        options.Validate();
        string? output = null;
        if (values.TryGetValue("--output", out var directory))
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("--output требует путь нового каталога.");
            output = Path.GetFullPath(directory);
        }
        return new(options, output);
    }

    public PreparedExperiment Prepare() => ExperimentInput.Prepare(Experiment,
        new InputSnapshot("generated-from-demo-cli", JsonSerializer.SerializeToUtf8Bytes(Experiment, ExperimentInput.Json)));
}
