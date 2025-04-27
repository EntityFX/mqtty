using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Counter;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Application
{
    public class Application<TOptions> : IApplication
    {
        private readonly Dictionary<string, IServer> _servers = new();
        private readonly Dictionary<string, IClient> _clients = new();

        public bool IsStarted { get; private set; }

        public INetwork? Network { get; private set; }
        public TOptions? Options { get; }
        public string ProtocolType { get; private set; } = string.Empty;

        public string Specification { get; private set; } = string.Empty;

        public Guid Id { get; private set; }

        public int Index { get; private set; }

        public string Address { get; private set; } = string.Empty;

        public string Name { get; private set; } = string.Empty;

        public string? Group { get; set; }
        public int? GroupAmount { get; set; }

        public NodeType NodeType => NodeType.Application;

        public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

        public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();

        public INode? Parent {  get; set; }
        public NetworkLoggerScope? Scope { get; set; }

        protected readonly INetworkSimulator NetworkGraph;

        internal ApplicationCounters counters;

        public CounterGroup Counters
        {
            get => counters;
            set
            {
                counters = (ApplicationCounters)value;
            }
        }

        public Application(int index, string name, string address, string protocolType, string specification,
            INetwork network, INetworkSimulator networkGraph, TOptions? options)
        {
            Address = address;
            Network = network;
            ProtocolType = protocolType;
            Specification = specification;
            Name = name;
            Id = Guid.NewGuid();
            Index = index;
            NetworkGraph = networkGraph;
            Options = options;

            counters = new ApplicationCounters(Name ?? string.Empty);
        }


        public virtual void Refresh()
        {
            Counters.Refresh(NetworkGraph.TotalTicks);
        }

        public virtual void Reset()
        {
            IsStarted = false;
        }

        public bool AddClient(IClient client)
        {
            if (client == null) throw new ArgumentNullException("client");

            if (_clients.ContainsKey(client.Address))
            {
                return false;
            }
            client.Parent = this;
            _clients[client.Name] = client;

            return true;
        }

        public bool RemoveClient(string client)
        {
            var clientNode = _clients.GetValueOrDefault(client);
            if (clientNode == null)
            {
                return false;
            }

            if (clientNode.IsConnected)
            {
                clientNode.Disconnect();
            }
            clientNode.Parent = null;
            return _clients.Remove(clientNode.Name);
        }

        public bool AddServer(IServer server)
        {
            if (server == null) throw new ArgumentNullException("server");

            if (_servers.ContainsKey(server.Name))
            {
                return false;
            }

            server.Parent = this;
            _servers[server.Name] = server;

            return true;
        }

        public bool RemoveServer(string id)
        {
            var server = _servers.GetValueOrDefault(id);
            if (server == null)
            {
                return false;
            }

            server.Parent = this;
            _servers.Remove(server.Name);

            return true;
        }

        public virtual Task StartAsync()
        {
            if (IsStarted) return Task.CompletedTask;

            var result = Network?.AddApplication(this);

            IsStarted = result == true;

            return Task.CompletedTask;
        }

        public virtual Task StopAsync()
        {
            if (!IsStarted) return Task.CompletedTask;

            var result = Network?.RemoveApplication(Address);

            IsStarted = result != true;

            return Task.CompletedTask;
        }
    }
}
