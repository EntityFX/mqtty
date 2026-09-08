namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal enum PendingMessageStatus
    {
        PendingToAcknowledge = 1,
        PendingToSend = 2,
        AwaitingPublishReceived = 3,
        AwaitingPublishRelease = 4,
        AwaitingPublishComplete = 5
    }
}
