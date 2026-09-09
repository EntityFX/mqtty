using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.MqttY.MqttRelay.App;

internal static class MqttRelayExperimentCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static int Run(string[] args)
    {
        try
        {
            var arguments = ParseArguments(args);
            var configPath = Path.GetFullPath(Required(arguments, "config"));
            var outputPath = Path.GetFullPath(Required(arguments, "output"));
            var configuration = JsonSerializer.Deserialize<MqttRelayExperimentRunConfiguration>(
                    File.ReadAllText(configPath), JsonOptions)
                ?? throw new InvalidDataException("Experiment configuration is empty.");
            configuration.Validate();

            var provenance = new ExperimentProvenance(
                ResolveCleanCommit(configuration.MqttYRepository),
                ResolveCleanCommit(configuration.MqttBenchmarkRepository),
                Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(Path.GetFullPath(configuration.BrokerProfilePath)))));
            var result = new MqttRelayExperimentRunner(configuration.Ticks, configuration.Network)
                .Run(configuration.Experiment, outputPath, provenance);

            Console.WriteLine($"Run directory: {result.RunDirectory}");
            Console.WriteLine($"Status: {result.Summary.Status}");
            if (result.Summary.Error != null) Console.Error.WriteLine(result.Summary.Error);
            if (result.Summary.Status != ExperimentRunStatus.Completed) return 2;
            return result.Summary.AcceptanceCriteria.Any(item => item.Status == "failed") ? 3 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Arguments must be supplied as --name value pairs.");
            var name = args[index][2..];
            if (!result.TryAdd(name, args[index + 1]))
                throw new ArgumentException($"Duplicate argument --{name}.");
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> arguments, string name) =>
        arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument --{name}.");

    private static string ResolveCleanCommit(string repository)
    {
        var fullPath = Path.GetFullPath(repository);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Git repository does not exist: {fullPath}");
        var dirty = RunGit(fullPath, "status", "--porcelain", "--untracked-files=no");
        if (!string.IsNullOrWhiteSpace(dirty))
            throw new InvalidOperationException(
                $"Git repository has tracked changes and cannot produce a reproducible manifest: {fullPath}");
        return RunGit(fullPath, "rev-parse", "HEAD").Trim();
    }

    private static string RunGit(string repository, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repository);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git failed for {repository}: {error.Trim()}");
        return output;
    }

    private static void PrintUsage() => Console.Error.WriteLine(
        "Usage: EntityFX.MqttY.MqttRelay.App experiment --config <json> --output <directory>");
}

internal sealed class MqttRelayExperimentRunConfiguration
{
    public MqttRelayExperimentOptions Experiment { get; set; } = new();
    public TicksOptions Ticks { get; set; } = new();
    public NetworkOptions Network { get; set; } = new();
    public string MqttYRepository { get; set; } = ".";
    public string MqttBenchmarkRepository { get; set; } = string.Empty;
    public string BrokerProfilePath { get; set; } = string.Empty;

    public void Validate()
    {
        Experiment.Validate();
        Ticks.Validate();
        Network.Validate();
        if (string.IsNullOrWhiteSpace(MqttYRepository) ||
            string.IsNullOrWhiteSpace(MqttBenchmarkRepository) ||
            string.IsNullOrWhiteSpace(BrokerProfilePath))
            throw new InvalidDataException(
                "MqttYRepository, MqttBenchmarkRepository and BrokerProfilePath are required.");
        if (!File.Exists(Path.GetFullPath(BrokerProfilePath)))
            throw new FileNotFoundException("Broker profile was not found.", BrokerProfilePath);
    }
}
