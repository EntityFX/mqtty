using EntityFX.MqttY.Helper;


var mqttRelayApp = new MqttRelayApp();

var networkSimulator = mqttRelayApp.ExecuteSimulation(false, 5, 2, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

networkSimulator = mqttRelayApp.ExecuteSimulation(false, 10, 2, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

networkSimulator = mqttRelayApp.ExecuteSimulation(false, 15, 2, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

networkSimulator = mqttRelayApp.ExecuteSimulation(true, 5, 2, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

networkSimulator = mqttRelayApp.ExecuteSimulation(false, 5, 20, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

networkSimulator = mqttRelayApp.ExecuteSimulation(true, 5, 20, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");


networkSimulator = mqttRelayApp.ExecuteSimulation(true, 6, 2, 3, 100);
PrintStats(networkSimulator);
Console.WriteLine("----------");

static void PrintStats(EntityFX.MqttY.Contracts.Network.INetworkSimulator networkSimulator)
{
    Console.WriteLine("Virtual time: {0}", networkSimulator.VirtualTime);
    Console.WriteLine("Real time: {0}", networkSimulator.RealTime);
    Console.WriteLine("Ticks: {0}", networkSimulator.TotalTicks);
    Console.WriteLine("Steps: {0}", networkSimulator.TotalSteps);
    Console.WriteLine("Errors: {0}", networkSimulator.Errors);
}

//Console.WriteLine(networkSimulator.Counters.PrintCounters());

