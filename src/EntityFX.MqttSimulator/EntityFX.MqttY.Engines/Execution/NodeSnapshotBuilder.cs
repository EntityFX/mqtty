using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Network.Diagnostics;
using EntityFX.MqttY.Engines.Contracts;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>Строит снимки статусов узлов из состояния симулятора (без продвижения модельного времени).</summary>
public static class NodeSnapshotBuilder
{
    public static IReadOnlyList<NodeStatusSnapshot> Build(INetworkSimulator simulator)
    {
        var snapshots = new List<NodeStatusSnapshot>();

        foreach (var network in simulator.Networks.Values)
        {
            snapshots.Add(new NodeStatusSnapshot
            {
                Id = network.Name,
                Name = network.Name,
                Type = "Network",
                Specification = network.NetworkType,
                Network = null,
                Connected = true,
                QueueLength = network.QueueSize,
                Counters = FlattenCounters(network)
            });
        }

        foreach (var server in simulator.Servers.Values)
        {
            snapshots.Add(new NodeStatusSnapshot
            {
                Id = server.Name,
                Name = server.Name,
                Type = "Server",
                Specification = server.Specification ?? string.Empty,
                Network = server.Network?.Name,
                Connected = server.IsStarted,
                QueueLength = QueueSize(server),
                Counters = FlattenCounters(server)
            });
        }

        foreach (var client in simulator.Clients.Values)
        {
            snapshots.Add(new NodeStatusSnapshot
            {
                Id = client.Name,
                Name = client.Name,
                Type = "Client",
                Specification = client.Specification ?? string.Empty,
                Network = client.Network?.Name,
                Connected = client.IsConnected,
                QueueLength = QueueSize(client),
                Counters = FlattenCounters(client)
            });
        }

        foreach (var application in simulator.Applications.Values)
        {
            snapshots.Add(new NodeStatusSnapshot
            {
                Id = application.Name,
                Name = application.Name,
                Type = "Application",
                Specification = application.Specification ?? string.Empty,
                Network = application.Network?.Name,
                Connected = true,
                QueueLength = QueueSize(application),
                Counters = FlattenCounters(application)
            });
        }

        return snapshots;
    }

    private static IReadOnlyDictionary<string, long> FlattenCounters(IWithCounters node)
    {
        var result = new Dictionary<string, long>();
        if (node.Counters == null)
        {
            return result;
        }

        foreach (var counter in node.Counters.Counters)
        {
            if (counter.Value is IConvertible convertible)
            {
                try
                {
                    result[counter.ShortName] = convertible.ToInt64(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    // нечисловые счётчики пропускаем
                }
            }
        }

        return result;
    }

    private static long QueueSize(object node)
    {
        if (node is not INodeQueueDiagnostics diagnostics) return 0;
        var snapshot = diagnostics.GetQueueSnapshot();
        return (long)snapshot.Incoming + snapshot.Outgoing;
    }
}
