using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PendingMessage
    {
        private int _status;

        public static PendingMessage FromPublish(PublishPacket message, PendingMessageStatus status,
            long? correlationId = null) => new()
        {
            Status = status,
            QualityOfService = message.QualityOfService,
            Duplicated = message.Duplicated,
            Retain = message.Retain,
            Topic = message.Topic,
            PacketId = message.PacketId,
            Payload = message.Payload,
            CorrelationId = correlationId
        };

        public PublishPacket ToPublishPacket() => new(Topic, QualityOfService, Retain, Duplicated, PacketId)
        {
            Payload = Payload
        };

        // Status is updated by QoS2 transitions that run outside the pending-list
        // lock; a volatile write makes the change visible to locked readers.
        public PendingMessageStatus Status
        {
            get => (PendingMessageStatus)Volatile.Read(ref _status);
            set => Volatile.Write(ref _status, (int)value);
        }

        public MqttQos QualityOfService { get; set; }

        public bool Duplicated { get; set; }

        public bool Retain { get; set; }

        public string Topic { get; set; } = string.Empty;

        public ushort? PacketId { get; set; }

        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public long? CorrelationId { get; set; }
    }
}
