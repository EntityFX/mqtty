using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.Sources.Clear();

IHostEnvironment env = builder.Environment;

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

builder.Services
    .Configure<NetworkGraphOptions>(
        builder.Configuration.GetSection("networkGraph"));

builder.Services
    .AddHostedService<Worker>()
    .AddTransient<IMonitoring, Monitoring>((sb) =>
    {
        var monitoring = new Monitoring();
        monitoring.Added += (sender, e) =>
            Console.WriteLine($"<{e.Date:u}>, {{{e.Type}}} {e.SourceType}[\"{e.From}\"] -> {e.DestinationType}[\"{e.To}\"]" +
                $"{(e.PacketSize > 0 ? $", Packet Size = {e.PacketSize}" : "")}.");
        return monitoring;
    })
    .AddTransient<IPathFinder, SimplePathFinder>()
    .AddTransient<INetworkGraph, NetworkGraph>();


using IHost host = builder.Build();


await host.RunAsync();


//var pathFinder = new SimplePathFinder();
//var networkGraph = new NetworkGraph(pathFinder, monitoring);



//var network1 = networkGraph.BuildNetwork("net1.local");
//var network2 = networkGraph.BuildNetwork("net2.local");
//var network3 = networkGraph.BuildNetwork("net3.local");
//var network4 = networkGraph.BuildNetwork("net4.local");
//var network5 = networkGraph.BuildNetwork("net5.local");
//var network6 = networkGraph.BuildNetwork("net6.local");
//var network7 = networkGraph.BuildNetwork("net7.local");
//var network8 = networkGraph.BuildNetwork("net8.local");
//var network9 = networkGraph.BuildNetwork("net9.local");
//var network10 = networkGraph.BuildNetwork("net10.local");


//network1.Link(network2);
//network1.Link(network3);
//network1.Link(network7);

//network2.Link(network4);
//network2.Link(network5);
//network3.Link(network5);
//network3.Link(network6);

//network4.Link(network8);
//network5.Link(network8);
//network5.Link(network9);
//network6.Link(network9);
//network7.Link(network10);

//network8.Link(network10);
//network9.Link(network10);

//var server = networkGraph.BuildServer("s1", "tcp", network10);
//server.PacketReceived += (sender, data) =>
//{

//};
//server.Start();


//var client1 = networkGraph.BuildClient("s2", "tcp", network1);
//client1.Connect("tcp://s1.net10.local");


//var nn = networkGraph.PathFinder.GetPathToNetwork("net1.local", "net10.local");

////var nn = network1.GetPathToNetworkWeighted("tcp://s1.net10.local", EntityFX.MqttY.Contracts.Network.NodeType.Server);


//client1.Send(new byte[] { 0x01, 0x02, 0x03 });


//server.Stop();