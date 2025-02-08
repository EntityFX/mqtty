namespace EntityFX.MqttY.Contracts.Network
{
    public interface INode
    {
        public Guid Id { get; }

        string Address { get; }

        string Name { get; }

        NodeType NodeType { get; }

        Task ReceiveAsync(Packet packet);

        Task SendAsync(Packet packet);
    }
}
