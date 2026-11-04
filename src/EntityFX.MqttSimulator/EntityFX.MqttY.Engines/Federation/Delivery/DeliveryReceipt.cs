namespace EntityFX.MqttY.Federation;

public sealed record DeliveryReceipt(int Receiver, long PublicationId, string Topic, long Tick, string PayloadSha256);
