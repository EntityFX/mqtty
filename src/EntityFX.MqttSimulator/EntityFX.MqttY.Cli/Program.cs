var monitoring = new Monitoring();
monitoring.Added += Monitoring_Added;

void Monitoring_Added(object? sender, EntityFX.MqttY.Contracts.Monitoring.MonitoringItem e)
{
    Console.WriteLine($"<{e.Date:u}>, {{{e.Type}}} {e.SourceType} ({e.From}) -> {e.DestinationType} ({e.To}), Packet Size = {e.PacketSize}.");
}

var network1 = new Network("net1.local", monitoring);
var network2 = new Network("net2.local", monitoring);
var network3 = new Network("net3.local", monitoring);
var network4 = new Network("net4.local", monitoring);
var network5 = new Network("net5.local", monitoring);
var network6 = new Network("net6.local", monitoring);
var network7 = new Network("net7.local", monitoring);
var network8 = new Network("net8.local", monitoring);
var network9 = new Network("net9.local", monitoring);
var network10 = new Network("net10.local", monitoring);


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

var server = new Server("tcp://s1.net10.local", network10, monitoring);
server.PacketReceived += (sender, data) =>
{

};
server.Start();


var client1 = new Client("tcp://c1.net1.local", network1, monitoring);
client1.Connect("tcp://s1.net10.local");

var nn = network1.GetPathToNetworkWeighted("tcp://s1.net10.local", EntityFX.MqttY.Contracts.Network.NodeType.Server);


client1.Send(new byte[] { 0x01, 0x02, 0x03});


server.Stop();