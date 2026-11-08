namespace EntityFX.MqttY.Plugin.Mqtt.Admission
{
    internal sealed class NoopBrokerAdmissionModel : IBrokerAdmissionModel
    {
        public static NoopBrokerAdmissionModel Instance { get; } = new();

        public BrokerAdmission TryAdmit(in PublishAdmissionContext context) =>
            new(Admit: true, DelayTicks: 0, BrokerRejectReason.None,
                ConditionalDeliveryLossRate: 0, Immediate: true);

        public void ClearRateLimiters()
        {
        }
    }
}
