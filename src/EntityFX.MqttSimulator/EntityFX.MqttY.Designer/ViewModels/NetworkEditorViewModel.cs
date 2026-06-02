using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Designer.Models;
using EntityFX.MqttY.Designer.Services;
using EntityFX.MqttY.Designer.Views;
using ReactiveUI;

namespace EntityFX.MqttY.Designer.ViewModels;

public class NetworkEditorViewModel : ReactiveObject
{
    private readonly IGraphLayoutService _graphLayoutService;
    private readonly IDialogService _dialogService;
    private NetworkDesignModel? _designModel;
    private GraphLayoutModel _layout = new();
    private List<GraphItem> _graphItems = new();
    private List<GraphLink> _graphLinks = new();
    private GraphItem? _selectedGraphItem;
    private NodeDesignModel? _selectedNode;
    private NetworkNodeDesignModel? _selectedNetwork;
    private NetworkTypeModel? _selectedNetworkType;
    private double _zoomLevel = 1.0;

    // Inspector visibility flags
    private bool _isNodeSelected;
    private bool _isNetworkSelected;
    private bool _isNetworkTypeSelected;
    private bool _isGraphItemSelected;

    public NetworkDesignModel? DesignModel
    {
        get => _designModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _designModel, value);
            if (value != null)
                RebuildGraph();
        }
    }

    public List<GraphItem> GraphItems
    {
        get => _graphItems;
        set => this.RaiseAndSetIfChanged(ref _graphItems, value);
    }

    public List<GraphLink> GraphLinks
    {
        get => _graphLinks;
        set => this.RaiseAndSetIfChanged(ref _graphLinks, value);
    }

    public GraphItem? SelectedGraphItem
    {
        get => _selectedGraphItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGraphItem, value);
            UpdateSelectionFromGraphItem(value);
        }
    }

    public NodeDesignModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNode, value);
            IsNodeSelected = value != null;
        }
    }

    public NetworkNodeDesignModel? SelectedNetwork
    {
        get => _selectedNetwork;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNetwork, value);
            IsNetworkSelected = value != null;
        }
    }

    public NetworkTypeModel? SelectedNetworkType
    {
        get => _selectedNetworkType;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedNetworkType, value);
            IsNetworkTypeSelected = value != null;
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => this.RaiseAndSetIfChanged(ref _zoomLevel, value);
    }

    // Inspector visibility
    public bool IsNodeSelected
    {
        get => _isNodeSelected;
        set => this.RaiseAndSetIfChanged(ref _isNodeSelected, value);
    }

    public bool IsNetworkSelected
    {
        get => _isNetworkSelected;
        set => this.RaiseAndSetIfChanged(ref _isNetworkSelected, value);
    }

    public bool IsNetworkTypeSelected
    {
        get => _isNetworkTypeSelected;
        set => this.RaiseAndSetIfChanged(ref _isNetworkTypeSelected, value);
    }

    public bool IsGraphItemSelected
    {
        get => _isGraphItemSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _isGraphItemSelected, value);
            HasNoSelection = !value;
        }
    }

    private bool _hasNoSelection = true;
    public bool HasNoSelection
    {
        get => _hasNoSelection;
        set => this.RaiseAndSetIfChanged(ref _hasNoSelection, value);
    }

    // Inspector data sources
    public List<NodeOptionType> NodeOptionTypes { get; } = new()
    {
        NodeOptionType.Client,
        NodeOptionType.Server,
        NodeOptionType.Application
    };

    public List<string> NetworkNames =>
        DesignModel?.Networks.Select(n => n.Name).ToList() ?? new();

    public List<string> NetworkTypeNames =>
        DesignModel?.NetworkTypes.Select(nt => nt.Name).ToList() ?? new();

    // Collections for data grids
    public ObservableCollection<NetworkTypeModel> NetworkTypes =>
        DesignModel?.NetworkTypes ?? new();

    public ObservableCollection<NetworkNodeDesignModel> Networks =>
        DesignModel?.Networks ?? new();

    public ObservableCollection<NodeDesignModel> Nodes =>
        DesignModel?.Nodes ?? new();

    // Commands
    public ICommand AddNetworkTypeCommand { get; }
    public ICommand DeleteNetworkTypeCommand { get; }
    public ICommand AddNetworkCommand { get; }
    public ICommand DeleteNetworkCommand { get; }
    public ICommand AddNodeCommand { get; }
    public ICommand AddServerCommand { get; }
    public ICommand AddApplicationCommand { get; }
    public ICommand DeleteNodeCommand { get; }
    public ICommand AddLinkCommand { get; }
    public ICommand DeleteLinkCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand GenerateRandomNetworkCommand { get; }
    public ICommand DeleteGraphItemCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand ArrangeCircularCommand { get; }
    public ICommand ArrangeForceDirectedCommand { get; }
    public ICommand ArrangeGridCommand { get; }
    public ICommand ArrangeHierarchicalCommand { get; }
    public ICommand AddGraphItemAtPositionCommand { get; }

    public NetworkEditorViewModel(IGraphLayoutService graphLayoutService, IDialogService dialogService)
    {
        _graphLayoutService = graphLayoutService;
        _dialogService = dialogService;

        AddNetworkTypeCommand = ReactiveCommand.Create(AddNetworkType);
        DeleteNetworkTypeCommand = ReactiveCommand.Create<NetworkTypeModel>(DeleteNetworkType);
        AddNetworkCommand = ReactiveCommand.Create(AddNetwork);
        DeleteNetworkCommand = ReactiveCommand.Create<NetworkNodeDesignModel>(DeleteNetwork);
        AddNodeCommand = ReactiveCommand.Create(AddNode);
        AddServerCommand = ReactiveCommand.Create(AddServer);
        AddApplicationCommand = ReactiveCommand.Create(AddApplication);
        DeleteNodeCommand = ReactiveCommand.Create<NodeDesignModel>(DeleteNode);
        AddLinkCommand = ReactiveCommand.Create<NetworkNodeDesignModel>(AddLink);
        DeleteLinkCommand = ReactiveCommand.Create<LinkDesignModel>(DeleteLink);
        ZoomInCommand = ReactiveCommand.Create(() => ZoomLevel = Math.Min(ZoomLevel + 0.1, 5.0));
        ZoomOutCommand = ReactiveCommand.Create(() => ZoomLevel = Math.Max(ZoomLevel - 0.1, 0.1));
        GenerateRandomNetworkCommand = ReactiveCommand.Create(GenerateRandomNetwork);
        DeleteGraphItemCommand = ReactiveCommand.Create<GraphItem>(DeleteGraphItem);
        DeleteSelectedCommand = ReactiveCommand.Create(DeleteSelected);
        ArrangeCircularCommand = ReactiveCommand.Create(ArrangeCircular);
        ArrangeForceDirectedCommand = ReactiveCommand.Create(ArrangeForceDirected);
        ArrangeGridCommand = ReactiveCommand.Create(ArrangeGrid);
        ArrangeHierarchicalCommand = ReactiveCommand.Create(ArrangeHierarchical);
        AddGraphItemAtPositionCommand = ReactiveCommand.Create<(GraphItemType type, Point position)>(items => AddGraphItemAtPosition(items.type, items.position));
    }

    public void RebuildGraph()
    {
        if (DesignModel == null) return;

        // Save current positions from graph items before recalculating
        SaveCurrentPositions();

        // Calculate layout for new items only (existing positions preserved)
        _layout = _graphLayoutService.CalculateLayout(DesignModel);
        BuildGraphItems();
        BuildGraphLinks();
    }

    /// <summary>
    /// Saves current positions of graph items so they persist across rebuilds.
    /// </summary>
    private void SaveCurrentPositions()
    {
        foreach (var item in _graphItems)
        {
            if (item.Tag is NetworkNodeDesignModel network)
                _layout.NetworkPositions[network.Name] = item.Position;
            else if (item.Tag is NodeDesignModel node)
                _layout.NodePositions[node.Name] = item.Position;
        }
    }

    /// <summary>
    /// Opens the appropriate editor dialog for the given graph item.
    /// Called on double-click from the canvas.
    /// </summary>
    public async Task EditGraphItemAsync(GraphItem item)
    {
        if (DesignModel == null) return;

        if (item.Tag is NodeDesignModel node)
        {
            var networks = Networks.Select(n => n.Name).ToList();
            var changed = await _dialogService.ShowNodeEditorAsync(node, networks);
            if (changed) RebuildGraph();
        }
        else if (item.Tag is NetworkNodeDesignModel network)
        {
            var networkTypes = NetworkTypes.Select(nt => nt.Name).ToList();
            var changed = await _dialogService.ShowNetworkEditorAsync(network, networkTypes);
            if (changed) RebuildGraph();
        }
    }

    /// <summary>
    /// Opens the network type editor dialog.
    /// </summary>
    public async Task EditNetworkTypeAsync(NetworkTypeModel networkType)
    {
        var changed = await _dialogService.ShowNetworkTypeEditorAsync(networkType);
        if (changed) RebuildGraph();
    }

    private void BuildGraphItems()
    {
        _graphItems.Clear();

        // Add networks
        foreach (var network in DesignModel!.Networks)
        {
            var pos = _layout.NetworkPositions.GetValueOrDefault(network.Name, new Point(100, 100));
            _graphItems.Add(new GraphItem
            {
                Name = network.Name,
                SubLabel = network.NetworkType,
                ItemType = GraphItemType.Network,
                Position = pos,
                Size = new Size(140, 70),
                Tag = network
            });
        }

        // Add nodes
        foreach (var node in DesignModel!.Nodes)
        {
            var pos = _layout.NodePositions.GetValueOrDefault(node.Name, new Point(300, 300));
            var itemType = node.Type switch
            {
                NodeOptionType.Server => GraphItemType.Server,
                NodeOptionType.Client => GraphItemType.Client,
                NodeOptionType.Application => GraphItemType.Application,
                _ => GraphItemType.Application
            };
            _graphItems.Add(new GraphItem
            {
                Name = node.Name,
                SubLabel = $"{node.Protocol} / {node.Specification}",
                ItemType = itemType,
                Position = pos,
                Size = new Size(120, 60),
                Tag = node
            });
        }

        GraphItems = new List<GraphItem>(_graphItems);
    }

    private void BuildGraphLinks()
    {
        _graphLinks.Clear();

        var itemByName = _graphItems.ToDictionary(i => i.Name);

        // Links between networks
        foreach (var network in DesignModel!.Networks)
        {
            if (!itemByName.TryGetValue(network.Name, out var fromItem)) continue;

            foreach (var link in network.Links)
            {
                if (link.TargetNetwork != null && itemByName.TryGetValue(link.TargetNetwork, out var toItem))
                {
                    _graphLinks.Add(new GraphLink
                    {
                        From = fromItem,
                        To = toItem,
                        Label = link.Weight?.ToString() ?? "",
                        Tag = link
                    });
                }
            }
        }

        // Connections from nodes to networks
        foreach (var node in DesignModel!.Nodes)
        {
            if (node.Network == null) continue;
            if (!itemByName.TryGetValue(node.Name, out var nodeItem)) continue;
            if (!itemByName.TryGetValue(node.Network, out var netItem)) continue;

            _graphLinks.Add(new GraphLink
            {
                From = nodeItem,
                To = netItem,
                Label = "",
                Tag = null
            });
        }

        GraphLinks = new List<GraphLink>(_graphLinks);
    }

    private void UpdateSelectionFromGraphItem(GraphItem? item)
    {
        SelectedNode = null;
        SelectedNetwork = null;
        SelectedNetworkType = null;
        IsGraphItemSelected = item != null;

        if (item?.Tag is NodeDesignModel node)
            SelectedNode = node;
        else if (item?.Tag is NetworkNodeDesignModel network)
            SelectedNetwork = network;
    }

    private void AddNetworkType()
    {
        var newType = new NetworkTypeModel
        {
            Name = $"type{NetworkTypes.Count + 1}",
            Speed = 125000000,
            RefreshTicks = 3,
            SendTicks = 5,
            QueueSize = 50000
        };
        NetworkTypes.Add(newType);
        SelectedNetworkType = newType;
    }

    private void DeleteNetworkType(NetworkTypeModel type)
    {
        NetworkTypes.Remove(type);
    }

    private void AddNetwork()
    {
        var newNetwork = new NetworkNodeDesignModel
        {
            Name = $"net{Networks.Count + 1}.local",
            Index = Networks.Count,
            NetworkType = NetworkTypes.FirstOrDefault()?.Name ?? "1g"
        };
        Networks.Add(newNetwork);

        // Add graph item without recalculating existing positions
        var pos = _layout.NetworkPositions.GetValueOrDefault(newNetwork.Name, new Point(100 + (Networks.Count - 1) * 50, 100 + (Networks.Count - 1) * 50));
        _graphItems.Add(new GraphItem
        {
            Name = newNetwork.Name,
            SubLabel = newNetwork.NetworkType,
            ItemType = GraphItemType.Network,
            Position = pos,
            Size = new Size(140, 70),
            Tag = newNetwork
        });
        GraphItems = new List<GraphItem>(_graphItems);
        BuildGraphLinks();
    }

    private void DeleteNetwork(NetworkNodeDesignModel network)
    {
        Networks.Remove(network);
        RebuildGraph();
    }

    private void AddNode()
    {
        var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
        var newNode = new NodeDesignModel
        {
            Name = $"node{Nodes.Count + 1}",
            Type = NodeOptionType.Client,
            Protocol = "mqtt",
            Specification = "mqtt-client",
            Network = targetNetwork
        };
        Nodes.Add(newNode);

        // Add graph item without recalculating existing positions
        var pos = _layout.NodePositions.GetValueOrDefault(newNode.Name, new Point(300 + (Nodes.Count - 1) * 30, 300 + (Nodes.Count - 1) * 30));
        _graphItems.Add(new GraphItem
        {
            Name = newNode.Name,
            SubLabel = $"{newNode.Protocol} / {newNode.Specification}",
            ItemType = GraphItemType.Client,
            Position = pos,
            Size = new Size(120, 60),
            Tag = newNode
        });
        GraphItems = new List<GraphItem>(_graphItems);
        BuildGraphLinks();
    }

    private void AddServer()
    {
        var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
        var newServer = new NodeDesignModel
        {
            Name = $"server{Nodes.Count(n => n.Type == NodeOptionType.Server) + 1}",
            Type = NodeOptionType.Server,
            Protocol = "mqtt",
            Specification = "mqtt-broker",
            Network = targetNetwork
        };
        Nodes.Add(newServer);

        var pos = _layout.NodePositions.GetValueOrDefault(newServer.Name, new Point(300 + (Nodes.Count - 1) * 30, 300 + (Nodes.Count - 1) * 30));
        _graphItems.Add(new GraphItem
        {
            Name = newServer.Name,
            SubLabel = $"{newServer.Protocol} / {newServer.Specification}",
            ItemType = GraphItemType.Server,
            Position = pos,
            Size = new Size(120, 60),
            Tag = newServer
        });
        GraphItems = new List<GraphItem>(_graphItems);
        BuildGraphLinks();
    }

    private void AddApplication()
    {
        var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
        var newApp = new NodeDesignModel
        {
            Name = $"app{Nodes.Count(n => n.Type == NodeOptionType.Application) + 1}",
            Type = NodeOptionType.Application,
            Protocol = "mqtt",
            Specification = "mqtt-relay",
            Network = targetNetwork
        };
        Nodes.Add(newApp);

        var pos = _layout.NodePositions.GetValueOrDefault(newApp.Name, new Point(300 + (Nodes.Count - 1) * 30, 300 + (Nodes.Count - 1) * 30));
        _graphItems.Add(new GraphItem
        {
            Name = newApp.Name,
            SubLabel = $"{newApp.Protocol} / {newApp.Specification}",
            ItemType = GraphItemType.Application,
            Position = pos,
            Size = new Size(120, 60),
            Tag = newApp
        });
        GraphItems = new List<GraphItem>(_graphItems);
        BuildGraphLinks();
    }

    private void DeleteNode(NodeDesignModel node)
    {
        Nodes.Remove(node);
        RebuildGraph();
    }

    private void AddLink(NetworkNodeDesignModel network)
    {
        var target = Networks.FirstOrDefault(n => n.Name != network.Name);
        if (target == null) return;

        network.Links.Add(new LinkDesignModel
        {
            TargetNetwork = target.Name,
            Weight = 1
        });
        RebuildGraph();
    }

    private void DeleteLink(LinkDesignModel link)
    {
        foreach (var network in Networks)
        {
            network.Links.Remove(link);
        }
        RebuildGraph();
    }

    /// <summary>
    /// Deletes a graph item (node or network) from the design model.
    /// Called from the canvas context menu.
    /// </summary>
    private void DeleteGraphItem(GraphItem item)
    {
        if (DesignModel == null) return;

        if (item.Tag is NodeDesignModel node)
        {
            Nodes.Remove(node);
        }
        else if (item.Tag is NetworkNodeDesignModel network)
        {
            // Remove all links referencing this network
            foreach (var net in Networks)
            {
                var linksToRemove = net.Links.Where(l => l.TargetNetwork == network.Name).ToList();
                foreach (var link in linksToRemove)
                    net.Links.Remove(link);
            }
            Networks.Remove(network);
        }

        RebuildGraph();
    }

    /// <summary>
    /// Generates a random network topology for testing/demo purposes.
    /// Creates random network types, networks with links, and nodes (clients/servers/apps).
    /// </summary>
    private void GenerateRandomNetwork()
    {
        if (DesignModel == null) return;

        var rng = new Random();

        // 1. Create 1-3 random network types
        var typeNames = new[] { "10g", "1g", "wifi5" };
        var typeSpeeds = new[] { 1250000000, 125000000, 12500000 };
        var typeCount = rng.Next(1, 4);
        for (int i = 0; i < typeCount; i++)
        {
            var existingNames = NetworkTypes.Select(nt => nt.Name).ToHashSet();
            var available = typeNames.Where(t => !existingNames.Contains(t)).ToList();
            if (available.Count == 0) break;
            var name = available[rng.Next(available.Count)];
            var idx = Array.IndexOf(typeNames, name);
            NetworkTypes.Add(new NetworkTypeModel
            {
                Name = name,
                Speed = typeSpeeds[idx],
                RefreshTicks = rng.Next(1, 5),
                SendTicks = rng.Next(3, 8),
                QueueSize = rng.Next(10000, 100000)
            });
        }

        // 2. Create 2-5 networks with random links
        var networkCount = rng.Next(2, 6);
        var networkNames = new List<string>();
        for (int i = 0; i < networkCount; i++)
        {
            var name = $"net{rng.Next(100, 999)}.local";
            while (networkNames.Contains(name))
                name = $"net{rng.Next(100, 999)}.local";
            networkNames.Add(name);

            var netType = NetworkTypes[rng.Next(NetworkTypes.Count)].Name;
            Networks.Add(new NetworkNodeDesignModel
            {
                Name = name,
                Index = i,
                NetworkType = netType
            });
        }

        // Create random links between networks
        foreach (var net in Networks.ToList())
        {
            var targets = Networks.Where(n => n.Name != net.Name).ToList();
            if (targets.Count == 0) continue;
            // 30% chance of link to each other network
            foreach (var target in targets)
            {
                if (rng.NextDouble() < 0.3)
                {
                    net.Links.Add(new LinkDesignModel
                    {
                        TargetNetwork = target.Name,
                        Weight = rng.Next(1, 10)
                    });
                }
            }
        }

        // 3. Create 3-10 nodes (mix of clients, servers, applications)
        var nodeCount = rng.Next(3, 11);
        var protocols = new[] { "mqtt", "net" };
        var clientSpecs = new[] { "mqtt-client", "mqtt-publisher", "mqtt-subscriber" };
        var serverSpecs = new[] { "mqtt-broker", "mqtt-relay" };
        var appSpecs = new[] { "mqtt-relay", "mqtt-forwarder" };

        for (int i = 0; i < nodeCount; i++)
        {
            var typeRoll = rng.NextDouble();
            NodeOptionType nodeType;
            string spec;
            string name;

            if (typeRoll < 0.5)
            {
                nodeType = NodeOptionType.Client;
                spec = clientSpecs[rng.Next(clientSpecs.Length)];
                name = $"client{rng.Next(100, 999)}";
            }
            else if (typeRoll < 0.8)
            {
                nodeType = NodeOptionType.Server;
                spec = serverSpecs[rng.Next(serverSpecs.Length)];
                name = $"server{rng.Next(100, 999)}";
            }
            else
            {
                nodeType = NodeOptionType.Application;
                spec = appSpecs[rng.Next(appSpecs.Length)];
                name = $"app{rng.Next(100, 999)}";
            }

            var network = Networks[rng.Next(Networks.Count)].Name;
            Nodes.Add(new NodeDesignModel
            {
                Name = name,
                Type = nodeType,
                Protocol = protocols[rng.Next(protocols.Length)],
                Specification = spec,
                Network = network,
                ConnectsToServer = nodeType == NodeOptionType.Client ? Networks.FirstOrDefault()?.Name : null,
                Quantity = rng.Next(1, 5)
            });
        }

        RebuildGraph();
    }

    /// <summary>
    /// Deletes the currently selected graph item (node or network).
    /// Called from the toolbar Delete button.
    /// </summary>
    private void DeleteSelected()
    {
        if (SelectedGraphItem != null)
        {
            DeleteGraphItem(SelectedGraphItem);
        }
        else if (SelectedNode != null)
        {
            DeleteNode(SelectedNode);
        }
        else if (SelectedNetwork != null)
        {
            DeleteNetwork(SelectedNetwork);
        }
    }

    /// <summary>
    /// Arranges all graph items in a circular layout without overlaps.
    /// </summary>
    private void ArrangeCircular()
    {
        if (DesignModel == null) return;

        _layout = _graphLayoutService.CalculateCircularLayout(DesignModel);
        BuildGraphItems();
        BuildGraphLinks();
    }

    /// <summary>
    /// Arranges all graph items using force-directed (gravitational) layout.
    /// Most connected element becomes the center; others are positioned by
    /// simulated physical forces (repulsion + attraction).
    /// </summary>
    private void ArrangeForceDirected()
    {
        if (DesignModel == null) return;

        _layout = _graphLayoutService.CalculateForceDirectedLayout(DesignModel);
        BuildGraphItems();
        BuildGraphLinks();
    }

    /// <summary>
    /// Arranges all graph items in a grid pattern.
    /// Networks in first rows, nodes under their networks.
    /// Cell size adapts to element dimensions.
    /// </summary>
    private void ArrangeGrid()
    {
        if (DesignModel == null) return;

        _layout = _graphLayoutService.CalculateGridLayout(DesignModel);
        BuildGraphItems();
        BuildGraphLinks();
    }

    /// <summary>
    /// Arranges all graph items in a hierarchical layout.
    /// Networks at the top level, nodes vertically below their network.
    /// </summary>
    private void ArrangeHierarchical()
    {
        if (DesignModel == null) return;

        _layout = _graphLayoutService.CalculateHierarchicalLayout(DesignModel);
        BuildGraphItems();
        BuildGraphLinks();
    }

    /// <summary>
    /// Adds a new graph item at the specified canvas position.
    /// Called from the canvas context menu.
    /// </summary>
    private void AddGraphItemAtPosition(GraphItemType type, Point position)
    {
        if (DesignModel == null) return;

        switch (type)
        {
            case GraphItemType.Network:
            {
                var newNetwork = new NetworkNodeDesignModel
                {
                    Name = $"net{Networks.Count + 1}.local",
                    Index = Networks.Count,
                    NetworkType = NetworkTypes.FirstOrDefault()?.Name ?? "1g"
                };
                Networks.Add(newNetwork);
                _graphItems.Add(new GraphItem
                {
                    Name = newNetwork.Name,
                    SubLabel = newNetwork.NetworkType,
                    ItemType = GraphItemType.Network,
                    Position = position,
                    Size = new Size(140, 70),
                    Tag = newNetwork
                });
                break;
            }
            case GraphItemType.Client:
            {
                var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
                var newNode = new NodeDesignModel
                {
                    Name = $"node{Nodes.Count + 1}",
                    Type = NodeOptionType.Client,
                    Protocol = "mqtt",
                    Specification = "mqtt-client",
                    Network = targetNetwork
                };
                Nodes.Add(newNode);
                _graphItems.Add(new GraphItem
                {
                    Name = newNode.Name,
                    SubLabel = $"{newNode.Protocol} / {newNode.Specification}",
                    ItemType = GraphItemType.Client,
                    Position = position,
                    Size = new Size(120, 60),
                    Tag = newNode
                });
                break;
            }
            case GraphItemType.Server:
            {
                var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
                var newServer = new NodeDesignModel
                {
                    Name = $"server{Nodes.Count(n => n.Type == NodeOptionType.Server) + 1}",
                    Type = NodeOptionType.Server,
                    Protocol = "mqtt",
                    Specification = "mqtt-broker",
                    Network = targetNetwork
                };
                Nodes.Add(newServer);
                _graphItems.Add(new GraphItem
                {
                    Name = newServer.Name,
                    SubLabel = $"{newServer.Protocol} / {newServer.Specification}",
                    ItemType = GraphItemType.Server,
                    Position = position,
                    Size = new Size(120, 60),
                    Tag = newServer
                });
                break;
            }
            case GraphItemType.Application:
            {
                var targetNetwork = SelectedNetwork?.Name ?? Networks.FirstOrDefault()?.Name;
                var newApp = new NodeDesignModel
                {
                    Name = $"app{Nodes.Count(n => n.Type == NodeOptionType.Application) + 1}",
                    Type = NodeOptionType.Application,
                    Protocol = "mqtt",
                    Specification = "mqtt-relay",
                    Network = targetNetwork
                };
                Nodes.Add(newApp);
                _graphItems.Add(new GraphItem
                {
                    Name = newApp.Name,
                    SubLabel = $"{newApp.Protocol} / {newApp.Specification}",
                    ItemType = GraphItemType.Application,
                    Position = position,
                    Size = new Size(120, 60),
                    Tag = newApp
                });
                break;
            }
        }

        GraphItems = new List<GraphItem>(_graphItems);
        BuildGraphLinks();
    }
}