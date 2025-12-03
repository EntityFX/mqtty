using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using System.Security.Cryptography;

namespace EntityFX.MqttY.Helper
{
    public abstract class MqttNetworkBuilderBase
    {
        private int _nextId = 0;

        protected readonly INetworkSimulator networkSimulator;

        public MqttNetworkBuilderBase(INetworkSimulator networkSimulator)
        {
            this.networkSimulator = networkSimulator;
        }

        protected abstract IClient CreateClient(TicksOptions ticksOptions, int ix, string name, string fullName, string address);

        protected abstract IServer CreateServer(TicksOptions ticksOptions, int ix, string fullName, string address);

        public Network.Network BuildChain(int branchingFactor, int length, int clientsPerNode, int serversPerNode,
            bool createLeafNodesOnly, TicksOptions ticksOptions)
        {
            if (branchingFactor < 1 || length < 1)
                throw new ArgumentException("Параметры должны быть положительными числами");

            Network.Network? previous = null;
            for (int i = 0; i < length; i++)
            {
                var network = CreateBranchingNetwork(branchingFactor, clientsPerNode, serversPerNode, createLeafNodesOnly,
                "net", new NetworkTypeOption()
                {
                    NetworkType = "eth",
                    RefreshTicks = 2,
                    SendTicks = 3,
                    Speed = 18750000
                }, ticksOptions);

                if (previous != null)
                {
                    network.Link(previous);
                }

                previous = network;

            }

            return previous!;
        }

        public Network.Network BuildTree(int branchingFactor, int depth, int clientsPerNode, int serversPerNode,
            bool createLeafNodesOnly, TicksOptions ticksOptions)
        {
            if (branchingFactor < 1 || depth < 1)
                throw new ArgumentException("Параметры должны быть положительными числами");

            return CreateDepthNetwork(branchingFactor, depth, clientsPerNode, serversPerNode, createLeafNodesOnly,
                "net", new NetworkTypeOption()
                {
                    NetworkType = "eth",
                    RefreshTicks = 2,
                    SendTicks = 3,
                    Speed = 18750000
                }, ticksOptions);
        }

        public Network.Network BuildRandomNodesTree(int branchingFactor, int depth,
            (int Min, int Max) clientsPerNode, (int Min, int Max) serversPerNode,
            bool createLeafNodesOnly, TicksOptions ticksOptions)
        {
            if (branchingFactor < 1 || depth < 1)
                throw new ArgumentException("Параметры должны быть положительными числами");

            return CreateRandomNodesNetwork(branchingFactor, depth, clientsPerNode, serversPerNode, createLeafNodesOnly,
                "net", new NetworkTypeOption()
                {
                    NetworkType = "eth",
                    RefreshTicks = 2,
                    SendTicks = 3,
                    Speed = 18750000
                }, ticksOptions);
        }

        private void CreateClients(Network.Network node, int clientsPerNode, TicksOptions ticksOptions)
        {
            for (int i = 0; i < clientsPerNode; i++)
            {
                var ix = _nextId++;
                var name = $"mqc{ix}";
                var fullName = $"{name}.{node.Name}";

                var address = $"mqtt://{name}";
                var client = CreateClient(ticksOptions, ix, name, fullName, address);
                node.AddClient(client);
                networkSimulator.AddClient(client);
            }
        }

        private Network.Network CreateBranchingNetwork(int branchingFactor,
            int clientsPerNode, int serversPerNode, bool createLeafNodesOnly, string namePrefix,
            NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
        {
            var ix = _nextId++;
            var nodeName = $"n{ix}.{namePrefix}";
            var node = new Network.Network(ix, nodeName, nodeName, "eth", networkTypeOption, ticksOptions);
            networkSimulator.AddNetwork(node);

            for (int i = 0; i < branchingFactor; i++)
            {
                ix = _nextId++;
                var childName = $"n{ix}.{nodeName}";
                var child = new Network.Network(ix, childName, childName, "eth", networkTypeOption, ticksOptions);
                networkSimulator.AddNetwork(child);
                CreateClients(child, clientsPerNode, ticksOptions);
                CreateServers(child, serversPerNode, ticksOptions);

                child.Link(node);
            }


            if (createLeafNodesOnly)
            {
                return node;
            }

            CreateClients(node, clientsPerNode, ticksOptions);
            CreateServers(node, serversPerNode, ticksOptions);

            return node;
        }

        private Network.Network CreateDepthNetwork(int branchingFactor, int depth,
            int clientsPerNode, int serversPerNode, bool createLeafNodesOnly, string namePrefix,
            NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
        {
            var ix = _nextId++;
            var name = $"n{ix}.{namePrefix}";
            var node = new Network.Network(ix, name, name, "eth", networkTypeOption, ticksOptions);
            networkSimulator.AddNetwork(node);
            if (depth > 1)
            {
                for (int i = 0; i < branchingFactor; i++)
                {
                    var child = CreateDepthNetwork(branchingFactor, depth - 1, clientsPerNode, serversPerNode,
                        createLeafNodesOnly, name,
                        networkTypeOption, ticksOptions);
                    child.Link(node);

                }
            }

            if (createLeafNodesOnly && depth > 1)
            {
                return node;
            }

            CreateClients(node, clientsPerNode, ticksOptions);
            CreateServers(node, serversPerNode, ticksOptions);

            return node;
        }

        private Network.Network CreateRandomNodesNetwork(int branchingFactor, int depth,
            (int Min, int Max) clientsPerNode,
            (int Min, int Max) serversPerNode,
            bool createLeafNodesOnly, string namePrefix,
            NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
        {
            var ix = _nextId++;
            var name = $"n{ix}.{namePrefix}";
            var node = new Network.Network(ix, name, name, "eth", networkTypeOption, ticksOptions);
            networkSimulator.AddNetwork(node);
            if (depth > 1)
            {
                for (int i = 0; i < branchingFactor; i++)
                {
                    var child = CreateRandomNodesNetwork(branchingFactor, depth - 1, clientsPerNode, serversPerNode,
                        createLeafNodesOnly, name,
                        networkTypeOption, ticksOptions);
                    child.Link(node);

                }
            }

            if (createLeafNodesOnly && depth > 1)
            {
                return node;
            }

            var clients = RandomNumberGenerator.GetInt32((clientsPerNode.Max - clientsPerNode.Min) + 1) + clientsPerNode.Min;
            var servers = RandomNumberGenerator.GetInt32((serversPerNode.Max - serversPerNode.Min) + 1) + serversPerNode.Min;

            CreateClients(node, clients, ticksOptions);
            CreateServers(node, servers, ticksOptions);

            return node;
        }

        private void CreateServers(Network.Network node, int serversPerNode, TicksOptions ticksOptions)
        {
            for (int i = 0; i < serversPerNode; i++)
            {
                var ix = _nextId++;
                var name = $"mqb{ix}";
                var fullName = $"{name}.{node.Name}";

                var address = $"mqtt://{name}";
                var broker = CreateServer(ticksOptions, ix, fullName, address);
                node.AddServer(broker);
                networkSimulator.AddServer(broker);
            }
        }
    }
}