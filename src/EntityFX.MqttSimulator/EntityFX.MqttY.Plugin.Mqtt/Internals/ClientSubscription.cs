﻿using EntityFX.MqttY.Plugin.Mqtt.Contracts;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class ClientSubscription
    {
        public string ClientId { get; set; } = string.Empty;

        public string TopicFilter { get; set; } = string.Empty;

        public MqttQos MaximumQualityOfService { get; set; }
    }
}
