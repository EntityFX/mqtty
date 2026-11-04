using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using EntityFX.MqttY.Utils;
using ModelNetwork = EntityFX.MqttY.Network.Network;
namespace EntityFX.MqttY.Federation;

public static class FederationTopologyBuilder
{
    public static FederationTopology Build(FederationInput input, FederationDeliveryLedger ledger, TransmissionLedger traffic)
    {
        var o = input.Options; var ticks = o.Ticks(); var edges = FederationGraph.Edges(o);
        var graph = new NetworkSimulator(new DijkstraPathFinder(), new NullNetworkLogger(), ticks, true);
        var result = new FederationTopology(graph, o.Mode == FederationMode.Single ? 1 : o.Brokers, o.Publishers, o.Receivers().Length);
        try
        {
            var generic = new GenericBuilder(); var evaluator = generic.GetMqttTopicEvaluator(); var packets = generic.GetMqttPacketManager(evaluator);
            var index = o.Brokers + 1;
            var sites = Enumerable.Range(0, FederationGraph.NetworkCount(o)).Select(i =>
                new ModelNetwork(i, FederationGraph.NetworkName(o, i), FederationGraph.NetworkName(o, i) + ".local", "ethernet", o.Network.Build(), ticks, true)).ToArray();
            foreach (var site in sites) graph.AddNetwork(site);
            foreach (var edge in edges) { graph.Link(sites[edge.A].Name, sites[edge.B].Name); graph.Link(sites[edge.B].Name, sites[edge.A].Name); }
            for (var b = 0; b < result.Brokers.Length; b++)
            {
                var count = Enumerable.Range(0, o.Publishers).Count(p => o.BrokerForSite(o.PublisherSite(p)) == b);
                result.Brokers[b] = new FederationBrokerBuilder(graph, packets, evaluator, input, count).Add(sites[o.NetworkForSite(b)], ticks, b, o.PhysicalLayout?.BrokerNames?[b]);
            }
            ObservedMqttClient Client(string name, int site, int target, bool automatic = false)
            {
                var client = new ObservedMqttClient(packets, index++, name, ticks, traffic, site, target);
                sites[o.NetworkForSite(site)].AddClient(client); graph.AddClient(client);
                if (!automatic) result.Connections.Add((client, target));
                return client;
            }
            for (var p = 0; p < o.Publishers; p++) result.Publishers[p] = Client(o.PhysicalLayout?.PublisherNames?[p] ?? $"publisher{p:D4}", o.PublisherSite(p), o.BrokerForSite(o.PublisherSite(p)));
            for (var r = 0; r < o.Receivers().Length; r++)
            {
                var site = o.Receivers()[r]; var receiverSite = site;
                var client = Client($"receiver{site:D4}", site, o.BrokerForSite(site)); result.Receivers[r] = client;
                client.MessageReceived += (_, m) => ledger.Receive(receiverSite, m.Topic, m.Payload, graph.TotalTicks);
                result.Subscriptions.Add((client, FederationTopics.DeliveredFilter));
            }
            if (o.Mode == FederationMode.Federation)
                for (var site = 0; site < o.Brokers; site++)
                {
                    var listener = Client($"fed{site}in", site, site);
                    result.Subscriptions.Add((listener, FederationTopics.InputFilter)); result.Subscriptions.Add((listener, FederationTopics.RoutingFilter));
                    var forwardingEdges = FederationGraph.ForwardingEdges(o);
                    var outputs = FederationGraph.Neighbors(site, forwardingEdges).Append(site).ToDictionary(target => target,
                        target => Client($"fed{site}to{target}", site, target));
                    var federator = new StaticFederator(site, o, ledger, new FloodingPolicy(forwardingEdges), outputs);
                    listener.MessageReceived += (_, m) => federator.Receive(m);
                    result.Federators.Add(federator);
                }
            if (o.Mode == FederationMode.Relay)
                for (var site = 0; site < o.Brokers; site++)
                    {
                        var localSite = site; var name = $"relay{site}";
                        var config = new MqttRelayConfiguration();
                        var map = new Dictionary<string, int>();
                        // Export only publications originating here. Imported publications are
                        // delivered locally and never exported again, even on a physical cycle.
                        for (var origin = 0; origin < o.Brokers; origin++)
                        {
                            var listen = "from" + origin;
                            map[name + "listen" + listen] = site;
                            config.ListenTopics[listen] = new()
                            {
                                Server = result.Brokers[site].Name,
                                Qos = (MqttQos)o.Qos,
                                Topics = new[] { site == origin ? FederationTopics.Input(origin) : FederationTopics.Relay(origin) }
                            };
                            var targets = new List<string>();
                            var destinations = origin == site
                                ? Enumerable.Range(0, o.Brokers).Where(target => target != site)
                                : Enumerable.Empty<int>();
                            if (o.Receivers().Contains(site)) destinations = destinations.Append(site);
                            foreach (var target in destinations)
                            {
                                var key = $"from{origin}to{target}";
                                targets.Add(key); map[name + "relay" + key] = target;
                                config.RelayTopics[key] = new()
                                {
                                    Server = result.Brokers[target].Name,
                                    TargetTopic = target == site ? FederationTopics.Delivered(origin) : FederationTopics.Relay(origin)
                                };
                            }
                            config.RouteMap[listen] = targets.ToArray();
                        }
                        var factory = generic.GetClientBuilder((a, clientName, c, d, e, f, g, h, i, j) => Client(clientName, localSite, map[clientName], true));
                        var relay = new MqttRelay(index++, name, "mqtt://" + name, "mqtt", "mqtt-relay", factory, evaluator, ticks, config, true);
                        sites[o.NetworkForSite(site)].AddApplication(relay); graph.AddApplication(relay); result.Relays.Add(relay);
                    }
            graph.UpdateRoutes();
            foreach (var relay in result.Relays) relay.Start();
            graph.UpdateRoutes();
            for (var b = 0; b < result.Brokers.Length; b++)
                ((IPublisherMeasurementSource)result.Brokers[b]).TrackPublishers(Enumerable.Range(0, o.Publishers)
                    .Where(p => o.BrokerForSite(o.PublisherSite(p)) == b).Select(p => result.Publishers[p].Name));
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
