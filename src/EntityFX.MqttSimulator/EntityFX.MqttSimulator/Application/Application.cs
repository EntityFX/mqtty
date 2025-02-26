using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Application
{
    public class Application : IApplication
    {
        public bool IsStarted { get; private set; }

        public INetwork? Network { get; private set; }

        public string ProtocolType { get; private set; } = string.Empty;

        public Guid Id { get; private set; }

        public int Index { get; private set; }

        public string Address { get; private set; } = string.Empty;

        public string Name { get; private set; } = string.Empty;

        public string? Group { get; set; }
        public int? GroupAmount { get; set; }

        public NodeType NodeType => throw new NotImplementedException();

        protected readonly INetworkGraph NetworkGraph;

        public Application(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph)
        {
            Address = address;
            Name = name;
            Id = Guid.NewGuid();
            Index = index;
            NetworkGraph = networkGraph;
        }

        public void Refresh()
        {
            Tick();
        }

        public virtual void Start()
        {
        }

        public virtual void Stop()
        {
        }

        public void Tick()
        {
            NetworkGraph.Tick(this);
        }
    }
}
