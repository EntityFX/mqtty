namespace EntityFX.MqttY.Contracts.Network
{
    public record Packet(
        string FromAddress, string ToAddress, 
        NodeType FromType, NodeType ToType, byte[] Payload);
}
