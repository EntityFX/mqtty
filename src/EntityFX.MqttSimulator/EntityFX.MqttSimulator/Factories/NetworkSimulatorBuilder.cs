using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

public class NetworkSimulatorBuilder : INetworkSimulatorBuilder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INodesBuilder _nodesBuilder;

    public string? OptionsPath { get; set; }

    public INetworkSimulator? NetworkSimulator { get; private set; }

    public NetworkSimulatorBuilder(IServiceProvider serviceProvider,
        INodesBuilder nodesBuilder)
    {
        this._serviceProvider = serviceProvider;
        this._nodesBuilder = nodesBuilder;
    }

    public IApplication? BuildApplication(
        int index, string name, string protocolType, string specification, INetwork network,
        NetworkOptions? networkTypeOption, TicksOptions? ticks,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = default)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var serverAddressFull = NetworkSimulator.GetAddress(name, protocolType, network.Address);

        var server = _nodesBuilder
            .ApplicationFactory[protocolType].Create(
                new NodeBuildOptions<NetworkBuildOption>(
                    _serviceProvider, NetworkSimulator, network, index, name, serverAddressFull,
                    group, groupAmount, protocolType, specification, null, new NetworkBuildOption()
                    {
                        Additional = additional,
                        NetworkTypeOption = networkTypeOption,
                        TicksOptions = ticks
                    })
                {
                    OptionsPath = OptionsPath
                });

        if (server == null)
        {
            return null;
        }

        NetworkSimulator.AddApplication(server);

        return server;
    }

    public IClient? BuildClient(int index, string name, string protocolType, string specification,
        INetwork network, TicksOptions? ticks,
        string? group = null, int? groupAmount = null,
        Dictionary<string, string[]>? additional = default)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var clientAddressFull = NetworkSimulator.GetAddress(name, protocolType, network.Address);

        var client = _nodesBuilder
            .ClientFactory[protocolType].Create(
                new NodeBuildOptions<NetworkBuildOption>(_serviceProvider,
                    NetworkSimulator, network, index, name, clientAddressFull, group, groupAmount, protocolType,
                    specification, null, new NetworkBuildOption()
                    {
                        TicksOptions = ticks,
                        NetworkTypeOption = new NetworkOptions()
                    }));

        if (client == null)
        {
            return null;
        }

        NetworkSimulator.AddClient(client);

        return client;
    }

    public TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification,
        INetwork network, TicksOptions ticks,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = default)
        where TClient : IClient
    {
        return (TClient?)BuildClient(index, name, protocolType, specification, network,
            ticks, group, groupAmount, additional);
    }

    public INetwork? BuildNetwork(int index, string name, string address, NetworkOptions networkTypeOption, TicksOptions ticks)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var network = _nodesBuilder
            .NetworkFactory.Create(
                new NodeBuildOptions<NetworkBuildOption>(
                    _serviceProvider, NetworkSimulator, null, index, name, address, null, null, "IP",
                    string.Empty, null, new NetworkBuildOption()
                    {
                        TicksOptions = ticks,
                        NetworkTypeOption = networkTypeOption,
                        Additional = new Dictionary<string, string[]>()
                    }
                    ));

        if (network == null)
        {
            return null;
        }

        NetworkSimulator.AddNetwork(network);

        return network;
    }

    public ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null,
        int? groupAmount = null,
        Dictionary<string, string[]>? additional = null)
    {
        return null;
    }

    public IServer? BuildServer(int index, string name, string protocolType, string specification,
        INetwork network, NetworkOptions networkTypeOption, TicksOptions ticks,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = null)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var serverAddressFull = NetworkSimulator.GetAddress(name, protocolType, network.Address);

        var server = _nodesBuilder
            .ServerFactory[protocolType].Create(
                new NodeBuildOptions<NetworkBuildOption>(
                    _serviceProvider, NetworkSimulator, network, index, name, serverAddressFull, group,
                    groupAmount, protocolType,
                    specification, null, new NetworkBuildOption()
                    {
                        Additional = additional,
                        TicksOptions = ticks,
                        NetworkTypeOption = networkTypeOption,
                    }));

        if (server == null)
        {
            return null;
        }

        NetworkSimulator.AddServer(server);

        return server;
    }

    public void Configure(INetworkSimulator networkSimulator, NetworkGraphOption option)
    {
        NetworkSimulator = networkSimulator;

        if (option.Networks.Any() != true)
        {
            return;
        }

        foreach (var networkOption in option.Networks)
        {
            var networkType = option.NetworkTypes.GetValueOrDefault(networkOption.Value.NetworkType) ?? new NetworkOptions()
            {
                TransferTicks = 3,
                Speed = 125000000
            };

            networkType.NetworkType = networkOption.Value.NetworkType;

            BuildNetwork(networkOption.Value.Index, networkOption.Key, networkOption.Key, networkType, option.Ticks);
        }

        ConfigureLinks(option);

        NetworkSimulator.UpdateRoutes();

        BuildNodes(option);

        ConfigureNodes(option);
    }

    private void ConfigureNodes(NetworkGraphOption option)
    {
        ConfigureServers(option);
        ConfigureClients(option);
        ConfigureApplications(option);
    }

    private void ConfigureApplications(NetworkGraphOption option)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var applications = option.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Application).ToArray();
        foreach (var application in applications)
        {
            var nodeApplication = NetworkSimulator.GetNode(application.Key, NodeType.Application) as IApplication;
            if (nodeApplication == null) continue;

            var bo = new NodeBuildOptions<NetworkBuildOption>(
                _serviceProvider,
                NetworkSimulator, nodeApplication.Network, nodeApplication.Index, nodeApplication.Name, nodeApplication.Address,
                nodeApplication.Group, nodeApplication.GroupAmount, nodeApplication.ProtocolType,
                nodeApplication.Specification ?? string.Empty,
                null, new NetworkBuildOption()
                {
                    TicksOptions = option.Ticks,
                    Additional = application.Value.Additional
                });

            _nodesBuilder.ApplicationFactory[application.Value.Protocol].Configure(bo, nodeApplication);
        }
    }

    private void ConfigureServers(NetworkGraphOption option)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var servers = option.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Server).ToArray();
        foreach (var node in servers)
        {
            var nodeServer = NetworkSimulator.GetNode(node.Key, NodeType.Server) as IServer;
            if (nodeServer == null) continue;

            var bo = new NodeBuildOptions<NetworkBuildOption>(
                _serviceProvider,
                NetworkSimulator, nodeServer.Network, nodeServer.Index, nodeServer.Name, nodeServer.Address,
                nodeServer.Group, nodeServer.GroupAmount, nodeServer.ProtocolType,
                nodeServer.Specification, node.Value.ConnectsToServer, new NetworkBuildOption()
                {
                    Additional = node.Value.Additional,
                    TicksOptions = option.Ticks
                });

            _nodesBuilder.ServerFactory[node.Value.Protocol].Configure(bo, nodeServer);
        }
    }

    private void ConfigureClients(NetworkGraphOption option)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var clients = option.Nodes.Where(
            nt => nt.Value.Type == NodeOptionType.Client).ToArray();
        foreach (var node in clients)
        {
            if (node.Value.Quantity > 1)
            {
                Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                    .ForEach(
                        (nc) =>
                        {
                            var nodeClient = NetworkSimulator.GetNode($"{node.Key}{nc}", NodeType.Client) as IClient;

                            if (nodeClient == null)
                            {
                                return;
                            }

                            var bo = new NodeBuildOptions<NetworkBuildOption>(
                                _serviceProvider,
                                NetworkSimulator, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                                nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType,
                                node.Value.Specification ?? string.Empty,
                                node.Value.ConnectsToServer, new NetworkBuildOption()
                                {
                                    Additional = node.Value.Additional,
                                    TicksOptions = option.Ticks
                                });
                            _nodesBuilder.ClientFactory[node.Value.Protocol].Configure(bo, nodeClient);
                        });
            }
            else
            {
                var nodeClient = NetworkSimulator.GetNode(node.Key, NodeType.Client) as IClient;
                if (nodeClient == null) continue;

                var bo = new NodeBuildOptions<NetworkBuildOption>(
                    _serviceProvider,
                    NetworkSimulator, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                    nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType,
                    node.Value.Specification ?? string.Empty, node.Value.ConnectsToServer, new NetworkBuildOption()
                    {
                        Additional = node.Value.Additional,
                        TicksOptions = option.Ticks
                    });
                _nodesBuilder.ClientFactory[node.Value.Protocol].Configure(bo, nodeClient);
            }
        }
    }

    private void BuildNodes(NetworkGraphOption option)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var index = 0;
        foreach (var node in option.Nodes)
        {
            var linkNetwork = NetworkSimulator.Networks.GetValueOrDefault(node.Value.Network ?? string.Empty);
            if (linkNetwork == null) continue;

            option.NetworkTypes.TryGetValue(linkNetwork.NetworkType, out var networkTypeOption);

            switch (node.Value.Type)
            {
                case NodeOptionType.Server:

                    BuildServer(index, node.Key, node.Value.Protocol ?? "tcp",
                        node.Value.Specification ?? "tcp-server",
                        linkNetwork,
                        networkTypeOption!, option.Ticks,
                        null, null, node.Value.Additional);
                    break;

                case NodeOptionType.Client:

                    if (node.Value.Quantity > 1)
                    {


                        Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                            .ForEach(
                                (nc) => BuildClient(
                                    index, $"{node.Key}{nc}",
                                    node.Value.Protocol ?? "tcp",
                                    node.Value.Specification ?? "tcp-client",
                                    linkNetwork, option.Ticks,
                                    node.Key, node.Value.Quantity, node.Value.Additional));
                    }
                    else
                    {
                        BuildClient(index, node.Key, node.Value.Protocol ?? "tcp",
                            node.Value.Specification ?? "tcp-client",
                            linkNetwork, option.Ticks,
                            null, null, node.Value.Additional);
                    }

                    break;
                case NodeOptionType.Application:
                    BuildApplication(index, node.Key, node.Value.Protocol ?? "tcp",
                        node.Value.Specification ?? "tcp-app",
                        linkNetwork, networkTypeOption, option.Ticks,
                        null, null, node.Value.Additional);
                    break;
            }

            index++;
        }
    }

    private void ConfigureLinks(NetworkGraphOption option)
    {
        if (NetworkSimulator == null)
        {
            throw new ArgumentNullException(nameof(NetworkSimulator));
        }

        var scope = NetworkSimulator.Monitoring.BeginScope(NetworkSimulator.TotalTicks, "Configure sourceNetwork links");
        foreach (var networkOption in option.Networks)
        {
            if (networkOption.Value?.Links?.Any() != true) continue;

            foreach (var link in networkOption.Value.Links)
            {
                if (link?.Network == null || !NetworkSimulator.Networks.ContainsKey(link.Network)) continue;

                var sourceNetwork = NetworkSimulator.Networks[networkOption.Key];
                var destinationNetwork = NetworkSimulator.Networks[link.Network];
                sourceNetwork.Scope = scope;
                destinationNetwork.Scope = scope;
                sourceNetwork.Link(destinationNetwork);
                sourceNetwork.Scope = null;
                destinationNetwork.Scope = null;
            }
        }

        NetworkSimulator.Monitoring.EndScope(NetworkSimulator.TotalTicks, scope);
    }
}
