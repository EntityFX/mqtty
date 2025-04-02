using EntityFX.MqttY.Contracts.Network;

public abstract partial class NodeBase
{
    public record ResponsePacket(NetworkPacket Packet, long SendTick, long ReceiveTick);
}
