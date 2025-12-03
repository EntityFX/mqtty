namespace EntityFX.MqttY.Contracts.Network
{
    public interface ISender : INode
    {
        bool Send(INetworkPacket packet);

        bool Receive(INetworkPacket packet);
    }
}
