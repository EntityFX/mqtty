namespace EntityFX.MqttY.Federation;

public sealed record TransmissionResult(long Tick, string Client, int Site, int Target, long PublicationId,
    bool Measurement, string Role, string Topic, int PayloadBytes, string PayloadSha256, int TopicPayloadBytes, bool Accepted);
