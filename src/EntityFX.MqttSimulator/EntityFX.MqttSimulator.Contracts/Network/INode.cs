namespace EntityFX.MqttY.Contracts.Network
{
    public interface INode
    {
        public Guid Id { get; }
        
        public int Index { get; }

        string Address { get; }

        string Name { get; }
        
        string? Group { get; set; }

        int? GroupAmount { get; set; }

        NodeType NodeType { get; }

        Task<Packet> ReceiveWithResponseAsync(Packet packet);

        Task<Packet> SendWithResponseAsync(Packet packet);

        Task SendAsync(Packet packet);

        Task ReceiveAsync(Packet packet);

        void Tick();

        void Refresh();

    }
}
