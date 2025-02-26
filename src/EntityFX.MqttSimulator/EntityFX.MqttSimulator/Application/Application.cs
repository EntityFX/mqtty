using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Application
{
    public class Application<TOptions> : IApplication
    {
        public bool IsStarted { get; private set; }

        public INetwork? Network { get; private set; }
        public TOptions? Options { get; }
        public string ProtocolType { get; private set; } = string.Empty;

        public Guid Id { get; private set; }

        public int Index { get; private set; }

        public string Address { get; private set; } = string.Empty;

        public string Name { get; private set; } = string.Empty;

        public string? Group { get; set; }
        public int? GroupAmount { get; set; }

        public NodeType NodeType => NodeType.Application;

        protected readonly INetworkGraph NetworkGraph;

        public Application(int index, string name, string address, string protocolType, 
            INetwork network, INetworkGraph networkGraph, TOptions? options)
        {
            Address = address;
            Network = network;
            ProtocolType = protocolType;
            Name = name;
            Id = Guid.NewGuid();
            Index = index;
            NetworkGraph = networkGraph;
            Options = options;
        }

        public void Refresh()
        {
            Tick();
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

        public void Tick()
        {
            NetworkGraph.Tick(this);
        }
    }
}
