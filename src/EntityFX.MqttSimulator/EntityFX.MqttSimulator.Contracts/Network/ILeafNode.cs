namespace EntityFX.MqttY.Contracts.Network
{
    public interface ILeafNode : INode
    {
        INetwork? Network { get; }

        INode? Parent { get; set; }

        public string ProtocolType { get; }

        public string Specification { get; }
    }
}
