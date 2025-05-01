﻿using EntityFX.MqttY.Plugin.Mqtt.Contracts;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PendingMessage
    {
        public PendingMessageStatus Status { get; set; }

        public MqttQos QualityOfService { get; set; }

        public bool Duplicated { get; set; }

        public bool Retain { get; set; }

        public string Topic { get; set; } = string.Empty;

        public ushort? PacketId { get; set; }

        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }
}
