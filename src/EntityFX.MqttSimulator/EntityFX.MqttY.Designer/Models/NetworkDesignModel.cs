using System.Collections.ObjectModel;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Design;

namespace EntityFX.MqttY.Designer.Models;

/// <summary>
/// Avalonia-facing adapter over <see cref="DesignDocument"/>. The document owns
/// the mapping and serialization logic; this model only exposes observable
/// collections and mutable editor fields for the UI.
/// </summary>
public class NetworkDesignModel
{
    public ObservableCollection<NetworkTypeModel> NetworkTypes { get; set; } = new();
    public ObservableCollection<NetworkNodeDesignModel> Networks { get; set; } = new();
    public ObservableCollection<NodeDesignModel> Nodes { get; set; } = new();
    public TicksOptions Ticks { get; set; } = new();
    public bool EnableCounters { get; set; }
    public int RandomSeed { get; set; }

    /// <summary>Optional persisted editor layout (positions and zoom).</summary>
    public GraphLayoutOption? Layout { get; set; }

    public DesignDocument ToDesignDocument() => new(
        Networks.Select(network => new DesignNetwork(
            network.Name,
            network.Index,
            network.NetworkType,
            network.Links
                .Where(link => !string.IsNullOrWhiteSpace(link.TargetNetwork))
                .Select(link => new DesignLink(link.TargetNetwork!, link.Weight))
                .ToArray())).ToArray(),
        Nodes.Select(node => new DesignNode(
            node.Name,
            node.Type,
            node.Protocol,
            node.Specification,
            node.Network,
            node.ConnectsToServer,
            node.Quantity,
            node.Index,
            node.Broker,
            node.Configuration,
            new Dictionary<string, string[]>(node.Additional),
            node.SendTicks)).ToArray(),
        NetworkTypes.Select(type => new DesignNetworkType(
            type.Name,
            type.Speed,
            type.RefreshTicks,
            type.QueueSize > 0 ? (int)Math.Min(type.QueueSize, int.MaxValue) : 50000)).ToArray(),
        Ticks,
        EnableCounters,
        RandomSeed,
        Layout);

    public static NetworkDesignModel FromDesignDocument(DesignDocument document)
    {
        var model = new NetworkDesignModel
        {
            Ticks = document.Ticks,
            EnableCounters = document.EnableCounters,
            RandomSeed = document.RandomSeed,
            Layout = document.Layout
        };

        foreach (var type in document.NetworkTypes)
        {
            model.NetworkTypes.Add(new NetworkTypeModel
            {
                Name = type.Name,
                Speed = type.Speed,
                RefreshTicks = type.TransferTicks,
                SendTicks = document.Ticks.OutgoingWaitTicks,
                QueueSize = type.QueueCapacity
            });
        }

        foreach (var network in document.Networks)
        {
            var networkModel = new NetworkNodeDesignModel
            {
                Name = network.Name,
                Index = network.Index,
                NetworkType = network.NetworkType
            };
            foreach (var link in network.Links)
            {
                networkModel.Links.Add(new LinkDesignModel
                {
                    TargetNetwork = link.TargetNetwork,
                    Weight = link.Weight
                });
            }
            model.Networks.Add(networkModel);
        }

        foreach (var node in document.Nodes)
        {
            model.Nodes.Add(new NodeDesignModel
            {
                Name = node.Name,
                Type = node.Type,
                Protocol = node.Protocol,
                Specification = node.Specification,
                Network = node.Network,
                ConnectsToServer = node.ConnectsToServer,
                Quantity = node.Quantity,
                Index = node.Index,
                Broker = node.Broker,
                Configuration = node.Configuration,
                Additional = new Dictionary<string, string[]>(node.Additional),
                SendTicks = node.SendTicks
            });
        }

        return model;
    }

    public NetworkGraphOption ToNetworkGraphOption() => ToDesignDocument().ToNetworkGraphOption();

    public static NetworkDesignModel FromNetworkGraphOption(NetworkGraphOption option) =>
        FromDesignDocument(DesignDocument.FromNetworkGraphOption(option));
}
