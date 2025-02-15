namespace EntityFX.MqttY.Contracts.Network
{
    public interface INode
    {
        public Guid Id { get; }
        
        public int Index { get; }

        string Address { get; }

        string Name { get; }
        
        string? Group { get; set; }

        NodeType NodeType { get; }

        Task<Packet> ReceiveAsync(Packet packet);

        Task<Packet> SendAsync(Packet packet);
    }
}
