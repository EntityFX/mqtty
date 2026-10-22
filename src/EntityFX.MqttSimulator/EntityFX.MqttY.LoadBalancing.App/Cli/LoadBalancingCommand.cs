namespace EntityFX.MqttY.LoadBalancing;

public static class LoadBalancingCommand
{
    public static int Execute(string[] args) => Execute(args, Console.Out, Console.Error);

    public static int Execute(string[] args, TextWriter standardOutput, TextWriter standardError)
    {
        var executing = false;
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h" or "help")
            {
                standardOutput.WriteLine("validate --config <json> | run/matrix --config <json> --output <new-directory>");
                standardOutput.WriteLine(DemoCommandOptions.HelpText);
                return 0;
            }
            if (args.Length > 0 && args[0] == "demo")
            {
                if (args.Length == 2 && args[1] is "--help" or "-h")
                {
                    standardOutput.WriteLine(DemoCommandOptions.HelpText);
                    return 0;
                }
                var demo = DemoCommandOptions.Parse(args.Skip(1).ToArray());
                var input = demo.Prepare();
                if (demo.OutputDirectory != null && (Directory.Exists(demo.OutputDirectory) || File.Exists(demo.OutputDirectory)))
                    throw new IOException("Choose a new output directory: " + demo.OutputDirectory);
                DemoConsoleView.WriteIntroduction(standardOutput, demo.Experiment);
                executing = true;
                var result = demo.OutputDirectory == null
                    ? new LoadBalancingExperimentRunner().RunInMemory(input)
                    : new LoadBalancingExperimentRunner().Run(input, demo.OutputDirectory);
                DemoConsoleView.WriteResult(standardOutput, input, result, demo.OutputDirectory);
                return result.Status == "completed" ? 0 : 2;
            }
            if (args.Length is not (3 or 5) || args[0] is not ("validate" or "run" or "matrix") || args[1] != "--config" ||
                args[0] == "validate" && args.Length != 3 || args[0] != "validate" && (args.Length != 5 || args[3] != "--output"))
                throw new ArgumentException("Usage: demo [--brokers N --mode roundRobin] | validate --config <json> | run/matrix --config <json> --output <new-directory>. Use --help.");
            var prepared = ExperimentInput.Load(args[2], args[0] == "matrix");
            if (args[0] == "validate")
            {
                var input = prepared.Single();
                standardOutput.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { input.Options, input.Assignments,
                    profileSha256 = input.Profile?.Source.Sha256,
                    warmupTicks = input.Options.ToTicks(input.Options.WarmupSeconds),
                    measurementTicks = input.Options.ToTicks(input.Options.MeasurementSeconds),
                    drainTimeoutTicks = input.Options.ToTicks(input.Options.DrainTimeoutSeconds) }, ExperimentInput.Json));
                return 0;
            }
            var output = Path.GetFullPath(args[4]);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output directory: " + output);
            executing = true;
            var runner = new LoadBalancingExperimentRunner();
            if (args[0] == "run") return runner.Run(prepared.Single(), output).Status == "completed" ? 0 : 2;
            ExperimentResultWriter.CreateOutput(output);
            var results = new List<object>();
            var failures = 0;
            for (var i = 0; i < prepared.Count; i++)
            {
                var input = prepared[i];
                var options = input.Options;
                var name = $"{i + 1:D3}-{options.BrokerType}-{options.Mode}-q{options.PublishQos}-s{options.Seed}";
                standardOutput.WriteLine($"[{i + 1}/{prepared.Count}] {name}");
                var result = runner.Run(input, Path.Combine(output, name));
                if (result.Status != "completed") failures++;
                results.Add(new { directory = name, options.BrokerType, options.Mode, options.PublishQos, options.Seed,
                    result.Status, result.BackendCompletedRps, result.UniqueGoodput, result.PublishP99Ms, result.ReceiverP99Ms });
                ExperimentResultWriter.WriteJson(Path.Combine(output, "matrix-summary.json"), new { planned = prepared.Count, completed = i + 1, failures, results });
            }
            ExperimentResultWriter.Seal(output);
            standardOutput.WriteLine($"Completed: {prepared.Count}; technical failures: {failures}; output: {output}");
            return failures == 0 ? 0 : 2;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            standardError.WriteLine(exception.Message);
            return executing ? 2 : 1;
        }
    }
}
