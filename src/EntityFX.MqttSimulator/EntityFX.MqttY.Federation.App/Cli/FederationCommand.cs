using System.Text.Json;
namespace EntityFX.MqttY.Federation;

public static class FederationCommand
{
    public const string Help = "demo [--brokers 3 --topology line|ring|star --mode single|relay|federation --publishers 12 --qos 1 --rps 1000 --seconds 0.2 --seed 42 --output <new>] | validate --config <json> | run/matrix --config <json> --output <new>";
    public static int Execute(string[] args) => Execute(args, Console.Out, Console.Error);
    public static int Execute(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var executing = false;
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "help" or "-h" || args.Length == 2 && args[0] == "demo" && args[1] is "--help" or "-h") { stdout.WriteLine(Help); return 0; }
            if (args.Length > 0 && args[0] == "demo")
            {
                var demo = FederationDemoOptions.Parse(args.Skip(1).ToArray()); var input = FederationInput.Prepare(demo.Options);
                if (demo.Output != null) CheckDisk(demo.Output); executing = true;
                var result = new FederationRunner().Run(input, demo.Output); FederationConsoleView.Write(stdout, demo.Options, result); return result.Status == "completed" ? 0 : 2;
            }
            if (args.Length is not (3 or 5) || args[0] is not ("validate" or "run" or "matrix") || args[1] != "--config" ||
                args[0] == "validate" && args.Length != 3 || args[0] != "validate" && (args.Length != 5 || args[3] != "--output")) throw new ArgumentException(Help);
            var prepared = FederationInput.Load(args[2]);
            if (args[0] == "validate")
            { stdout.WriteLine(JsonSerializer.Serialize(new { prepared.Options, prepared.ProfileSha256, edges = FederationGraph.Edges(prepared.Options) }, FederationInput.Json)); return 0; }
            var output = Path.GetFullPath(args[4]); CheckDisk(output);
            if (args[0] == "run")
            { executing = true; var result = new FederationRunner().Run(prepared, output); FederationConsoleView.Write(stdout, prepared.Options, result); return result.Status == "completed" ? 0 : 2; }
            var cases = (from mode in Enum.GetValues<FederationMode>()
                         from qos in new[] { 0, 1, 2 }
                         from seed in new[] { 42, 43, 44 }
                         select prepared.WithOptions(prepared.Options with { Mode = mode, Qos = qos, Seed = seed })).ToArray();
            FederationResultWriter.Claim(output); executing = true; var results = new List<object>(); var failures = 0;
            for (var i = 0; i < cases.Length; i++)
            {
                var input = cases[i]; var o = input.Options; var name = $"{i + 1:D3}-{o.Mode}-q{o.Qos}-s{o.Seed}"; CheckDisk(Path.Combine(output, name));
                stdout.WriteLine($"[{i + 1}/{cases.Length}] {name}"); var result = new FederationRunner().Run(input, Path.Combine(output, name));
                if (result.Status != "completed") failures++;
                results.Add(new { directory = name, o.Mode, o.Qos, o.Seed, result.Status, result.Error, result.SourceCompletedRps, result.UniqueGoodput, result.DeliveryLoss });
                FederationResultWriter.Json(Path.Combine(output, "matrix-summary.json"), new { planned = cases.Length, completed = i + 1, failures, results });
            }
            FederationResultWriter.Seal(output); stdout.WriteLine($"Завершено {cases.Length}; технических отказов {failures}."); return failures == 0 ? 0 : 2;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { stderr.WriteLine(ex.Message); return executing ? 2 : 1; }
    }
    private static void CheckDisk(string output)
    {
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output directory: " + output);
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(output))!);
        if (drive.AvailableFreeSpace < 5L * 1024 * 1024 * 1024) throw new IOException("At least 5 GiB must remain on the result disk.");
    }
}
