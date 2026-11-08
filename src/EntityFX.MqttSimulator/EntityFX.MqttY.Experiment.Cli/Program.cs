using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.MqttRelay.App;

// One experiment CLI host. Engine-specific command implementations stay in
// their existing assemblies; this host only dispatches and keeps exit codes
// (0 = ok, 1 = usage/validation error, 2 = technical failure).
if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    PrintHelp();
    return 0;
}

var engine = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

return engine switch
{
    "lb" or "loadbalancing" => LoadBalancingCommand.Execute(rest),
    "fed" or "federation" => FederationCommand.Execute(rest),
    "relay" => ExecuteRelay(rest),
    _ => UnknownEngine(engine)
};

static int ExecuteRelay(string[] rest)
{
    if (rest.Length == 0 || rest[0] is "--help" or "-h" or "help")
    {
        Console.WriteLine("relay experiment <options> | relay calibrate <options>");
        return 0;
    }

    return rest[0] switch
    {
        "experiment" => MqttRelayExperimentCommand.Run(rest.Skip(1).ToArray()),
        "calibrate" => MqttRelayExperimentCommand.RunCalibration(rest.Skip(1).ToArray()),
        _ => UnknownRelayCommand(rest[0])
    };
}

static int UnknownEngine(string engine)
{
    Console.Error.WriteLine($"Unknown engine '{engine}'. Use lb, fed or relay.");
    PrintHelp();
    return 1;
}

static int UnknownRelayCommand(string command)
{
    Console.Error.WriteLine($"Unknown relay command '{command}'. Use experiment or calibrate.");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("MqttY experiment CLI");
    Console.WriteLine("  lb  demo|validate|run|matrix ...");
    Console.WriteLine("  fed demo|validate|run|matrix ...");
    Console.WriteLine("  relay experiment|calibrate ...");
    Console.WriteLine("Exit codes: 0 = completed, 1 = usage/validation error, 2 = technical failure.");
}
