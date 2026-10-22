using System.Globalization;
namespace EntityFX.MqttY.Federation;

public sealed record FederationDemoOptions(FederationOptions Options, string? Output)
{
    public static FederationDemoOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string> { "--brokers", "--topology", "--mode", "--publishers", "--qos", "--rps", "--seconds", "--seed", "--output" };
        if (args.Length % 2 != 0) throw new ArgumentException("Expected --option value pairs.");
        for (var i = 0; i < args.Length; i += 2) if (!allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or repeated option: " + args[i]);
        string S(string key, string fallback) => values.GetValueOrDefault(key, fallback);
        int I(string key, int fallback) => int.Parse(S(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        double D(string key, double fallback) => double.Parse(S(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!Enum.TryParse<FederationMode>(S("--mode", "federation"), true, out var mode) || !Enum.IsDefined(typeof(FederationMode), mode)) throw new ArgumentException("Unknown mode.");
        var options = new FederationOptions
        {
            Brokers = I("--brokers", 3),
            Topology = S("--topology", "ring"),
            Mode = mode,
            Publishers = I("--publishers", 12),
            Qos = I("--qos", 1),
            OfferedRps = D("--rps", 1000),
            MeasurementSeconds = D("--seconds", .2),
            Seed = I("--seed", 42)
        };
        options.Validate(); return new(options, values.GetValueOrDefault("--output"));
    }
}
