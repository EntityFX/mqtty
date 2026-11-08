using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class ClientSubscription
    {
        private int _maximumQualityOfService;

        public string ClientId { get; set; } = string.Empty;

        public string TopicFilter { get; set; } = string.Empty;

        // The fanout snapshot holds subscription objects and reads QoS without
        // the session lock; volatile access makes resubscribe updates visible.
        public MqttQos MaximumQualityOfService
        {
            get => (MqttQos)Volatile.Read(ref _maximumQualityOfService);
            set => Volatile.Write(ref _maximumQualityOfService, (int)value);
        }
    }
}
