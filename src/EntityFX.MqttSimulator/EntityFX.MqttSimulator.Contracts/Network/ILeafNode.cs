namespace EntityFX.MqttY.Contracts.Network
{
    public interface ILeafNode : INode
    {
        INetwork? Network { get; }
    }
}
