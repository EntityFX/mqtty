using System.Collections.ObjectModel;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Designer.Models;

public class NetworkDesignModel
{
    public ObservableCollection<NetworkTypeModel> NetworkTypes { get; set; } = new();
    public ObservableCollection<NetworkNodeDesignModel> Networks { get; set; } = new();
    public ObservableCollection<NodeDesignModel> Nodes { get; set; } = new();
    public TicksOptions Ticks { get; set; } = new();
    public bool EnableCounters { get; set; }

    /// <summary>Optional persisted editor layout (positions and zoom).</summary>
    public GraphLayoutOption? Layout { get; set; }

    public NetworkGraphOption ToNetworkGraphOption()
    {
        var option = new NetworkGraphOption
        {
            Ticks = Ticks,
            EnableCounters = EnableCounters,
            Layout = Layout,
            NetworkTypes = new SortedDictionary<string, NetworkOptions>(),
            Networks = new SortedDictionary<string, NetworkNodeOption>(),
            Nodes = new SortedDictionary<string, NodeOption>()
        };

        foreach (var nt in NetworkTypes)
        {
            option.NetworkTypes[nt.Name] = new NetworkOptions
            {
                Speed = nt.Speed,
                TransferTicks = nt.RefreshTicks,
                QueueCapacity = nt.QueueSize > 0 ? (int)Math.Min(nt.QueueSize, int.MaxValue) : 50000,
                NetworkType = nt.Name
            };
        }

        foreach (var n in Networks)
        {
            var nodeOption = new NetworkNodeOption
            {
                Index = n.Index,
                NetworkType = n.NetworkType,
                Links = n.Links.Select(l => new NetworkLinkOption
                {
                    Network = l.TargetNetwork,
                    W = l.Weight
                }).ToArray()
            };
            option.Networks[n.Name] = nodeOption;
        }

        foreach (var node in Nodes)
        {
            var nodeOption = new NodeOption
            {
                Type = node.Type,
                Protocol = node.Protocol,
                Specification = node.Specification,
                Network = node.Network,
                ConnectsToServer = node.ConnectsToServer,
                Quantity = node.Quantity,
                Index = node.Index,
                Configuration = node.Configuration
            };
            option.Nodes[node.Name] = nodeOption;
        }

        return option;
    }

    public static NetworkDesignModel FromNetworkGraphOption(NetworkGraphOption option)
    {
        var model = new NetworkDesignModel
        {
            Ticks = option.Ticks,
            EnableCounters = option.EnableCounters,
            Layout = option.Layout
        };

        foreach (var (name, nt) in option.NetworkTypes)
        {
            model.NetworkTypes.Add(new NetworkTypeModel
            {
                Name = name,
                Speed = nt.Speed,
                RefreshTicks = nt.TransferTicks,
                SendTicks = option.Ticks.OutgoingWaitTicks,
                QueueSize = nt.QueueCapacity
            });
        }

        foreach (var (name, n) in option.Networks)
        {
            var networkModel = new NetworkNodeDesignModel
            {
                Name = name,
                Index = n.Index,
                NetworkType = n.NetworkType
            };
            foreach (var link in n.Links)
            {
                networkModel.Links.Add(new LinkDesignModel
                {
                    TargetNetwork = link.Network,
                    Weight = link.W
                });
            }
            model.Networks.Add(networkModel);
        }

        foreach (var (name, node) in option.Nodes)
        {
            model.Nodes.Add(new NodeDesignModel
            {
                Name = name,
                Type = node.Type,
                Protocol = node.Protocol,
                Specification = node.Specification,
                Network = node.Network,
                ConnectsToServer = node.ConnectsToServer,
                Quantity = node.Quantity,
                Index = node.Index,
                Configuration = node.Configuration
            });
        }

        return model;
    }
}