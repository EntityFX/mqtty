var monitoring = new Monitoring();
monitoring.Added += Monitoring_Added;

void Monitoring_Added(object? sender, EntityFX.MqttY.Contracts.Monitoring.MonitoringItem e)
{
    Console.WriteLine($"<{e.Date:u}>, {{{e.Type}}} {e.SourceType} ({e.From}) -> {e.DestinationType} ({e.To}), Packet Size = {e.PacketSize}.");
}

var pathFinder = new SimplePathFinder();
var networkGraph = new NetworkGraph(pathFinder, monitoring);



var network1 = networkGraph.BuildNetwork("net1.local");
var network2 = networkGraph.BuildNetwork("net2.local");
var network3 = networkGraph.BuildNetwork("net3.local");
var network4 = networkGraph.BuildNetwork("net4.local");
var network5 = networkGraph.BuildNetwork("net5.local");
var network6 = networkGraph.BuildNetwork("net6.local");
var network7 = networkGraph.BuildNetwork("net7.local");
var network8 = networkGraph.BuildNetwork("net8.local");
var network9 = networkGraph.BuildNetwork("net9.local");
var network10 = networkGraph.BuildNetwork("net10.local");


network1.Link(network2);
network1.Link(network3);
network1.Link(network7);

network2.Link(network4);
network2.Link(network5);
network3.Link(network5);
network3.Link(network6);

network4.Link(network8);
network5.Link(network8);
network5.Link(network9);
network6.Link(network9);
network7.Link(network10);

network8.Link(network10);
network9.Link(network10);

var server = networkGraph.BuildServer("tcp://s1.net10.local", network10);
server.PacketReceived += (sender, data) =>
{

};
server.Start();


var client1 = networkGraph.BuildClient("tcp://c1.net1.local", network1);
client1.Connect("tcp://s1.net10.local");


var nn = networkGraph.PathFinder.GetPathToNetwork("net1.local", "net10.local");

//var nn = network1.GetPathToNetworkWeighted("tcp://s1.net10.local", EntityFX.MqttY.Contracts.Network.NodeType.Server);


client1.Send(new byte[] { 0x01, 0x02, 0x03});


server.Stop();