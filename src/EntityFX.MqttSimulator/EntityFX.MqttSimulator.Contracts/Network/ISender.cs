namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        bool Send(NetworkPacket packet);

        bool Receive(NetworkPacket packet);
    }
}
