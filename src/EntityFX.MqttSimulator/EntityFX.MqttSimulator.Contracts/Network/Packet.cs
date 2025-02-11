namespace EntityFX.MqttY.Contracts.Network
{
    public record Packet(
        string From, string To, 
        NodeType FromType, NodeType ToType, byte[] Payload, string? Category = null, Guid? scope = null);
}
