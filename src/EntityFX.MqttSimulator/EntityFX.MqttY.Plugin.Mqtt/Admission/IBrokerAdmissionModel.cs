using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Admission
{
    internal readonly record struct PublishAdmissionContext(
        string Publisher,
        MqttQos Qos,
        ushort? PacketId,
        int MessageBytes,
        long LocalPublishSequence,
        long Tick,
        int ClientCount);

    internal enum BrokerRejectReason
    {
        None,
        RateLimit,
        PublishFailure
    }

    internal readonly record struct BrokerAdmission(
        bool Admit,
        int DelayTicks,
        BrokerRejectReason RejectReason,
        double ConditionalDeliveryLossRate,
        bool Immediate);

    internal interface IBrokerAdmissionModel
    {
        BrokerAdmission TryAdmit(in PublishAdmissionContext context);

        void ClearRateLimiters();
    }
}
