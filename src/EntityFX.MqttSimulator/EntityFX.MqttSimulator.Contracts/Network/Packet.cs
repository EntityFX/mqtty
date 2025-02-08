namespace EntityFX.MqttY.Contracts.Network
{
    public record Packet(
        string FromAddress, string To, 
        NodeType FromType, NodeType ToType, byte[] Payload);
}
