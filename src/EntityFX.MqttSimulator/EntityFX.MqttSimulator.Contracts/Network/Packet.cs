namespace EntityFX.MqttY.Contracts.Network
{
    public record Packet(string From, string To, NodeType SourceType, NodeType DestinationType, byte[] packet);
}
