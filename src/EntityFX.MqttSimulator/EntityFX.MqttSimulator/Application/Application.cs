using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Counter;
using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Application
{
    public class ApplicationBase : IApplication
    {
        private readonly Dictionary<string, IServer> _servers = new();
        private readonly Dictionary<string, IClient> _clients = new();

        public bool IsStarted { get; private set; }

        public INetwork? Network { get; internal set; }
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

        public INode? Parent { get; set; }
        public NetworkLoggerScope? Scope { get; set; }

        protected ApplicationCounters counters;

        public CounterGroup Counters
        {
            get => counters;
            set => counters = (ApplicationCounters)value;
        }

        public INetworkSimulator? NetworkSimulator { get; internal set; }

        public ApplicationBase(int index, string name, string address, string protocolType, string specification,
            TicksOptions ticksOptions)
        {
            Address = address;
            ProtocolType = protocolType;
            Specification = specification;
            Name = name;
            Id = Guid.NewGuid();
            Index = index;
            counters = new ApplicationCounters(Name ?? string.Empty, ticksOptions.CounterHistoryDepth);
        }


        public virtual void Refresh()
        {
            if (NetworkSimulator == null) return;

            Counters.Refresh(NetworkSimulator.TotalTicks);
        }

        public virtual void Reset()
        {
            IsStarted = false;
        }

        public bool AddClient(IClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

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
            if (server == null) throw new ArgumentNullException(nameof(server));

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

        public virtual void Start()
        {
            if (IsStarted) return;

            var result = Network?.AddApplication(this);

            IsStarted = result == true;
        }

        public virtual void Stop()
        {
            if (!IsStarted) return;

            var result = Network?.RemoveApplication(Address);

            IsStarted = result != true;
        }
    }

    public class Application<TOptions> : ApplicationBase
    {
        public TOptions? Options { get; }

        public Application(int index, string name, string address, string protocolType, string specification,
            TicksOptions ticksOptions, TOptions? options)
            : base(index, name, address, protocolType, specification, ticksOptions)
        {
            Options = options;
        }
    }
}
