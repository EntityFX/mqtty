using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class SubscribeFormatter : Formatter<SubscribePacket>
    {
        readonly IMqttTopicEvaluator _topicEvaluator;

        public SubscribeFormatter(IMqttTopicEvaluator topicEvaluator)
        {
            this._topicEvaluator = topicEvaluator;
        }

        public override MqttPacketType PacketType { get { return MqttPacketType.Subscribe; } }

        protected override SubscribePacket Read(byte[] bytes)
        {
            ValidateHeaderFlag(bytes, t => t == MqttPacketType.Subscribe, 0x02);

            ReadOnlySpan<byte> packet = bytes;
            _ = Encoding.DecodeRemainingLength(packet, out var remainingLengthBytesLength);

            var packetIdentifierStartIndex = remainingLengthBytesLength + 1;
            var packetIdentifier = packet.Slice(packetIdentifierStartIndex, 2).ToUInt16();

            var headerLength = 1 + remainingLengthBytesLength + 2;
            var subscriptions = GetSubscriptions(packet, headerLength);

            return new SubscribePacket(packetIdentifier, subscriptions);
        }

        protected override byte[] Write(SubscribePacket packet)
        {
            if (packet.Subscriptions == null || packet.Subscriptions.Length == 0)
                throw new MqttException("SubscribeFormatter_MissingTopicFilterQosPair");

            var payloadLength = 0;
            foreach (var subscription in packet.Subscriptions)
            {
                if (string.IsNullOrEmpty(subscription.TopicFilter))
                    throw new MqttException("SubscribeFormatter_MissingTopicFilterQosPair");

                if (!_topicEvaluator.IsValidTopicFilter(subscription.TopicFilter))
                {
                    var error = string.Format("SubscribeFormatter_InvalidTopicFilter {0}", subscription.TopicFilter);

                    throw new MqttException(error);
                }

                payloadLength += MqttProtocolConsts.StringPrefixLength +
                    Encoding.GetStringByteCount(subscription.TopicFilter) + 1;
            }

            var remainingLength = 2 + payloadLength;
            var remainingLengthBytes = MqttEncoder.GetRemainingLengthByteCount(remainingLength);
            var bytes = new byte[1 + remainingLengthBytes + remainingLength];
            var destination = bytes.AsSpan();

            var flags = 0x02;
            var type = Convert.ToInt32(MqttPacketType.Subscribe) << 4;
            destination[0] = Convert.ToByte(flags | type);
            var written = 1 + MqttEncoder.WriteRemainingLength(
                destination.Slice(1, remainingLengthBytes), remainingLength);

            MqttEncoder.WriteInteger(destination.Slice(written), packet.PacketId);
            written += 2;

            foreach (var subscription in packet.Subscriptions)
            {
                Encoding.WriteString(destination.Slice(written), subscription.TopicFilter);
                written += MqttProtocolConsts.StringPrefixLength +
                    Encoding.GetStringByteCount(subscription.TopicFilter);
                destination[written++] = Convert.ToByte(subscription.MaximumQualityOfService);
            }

            return bytes;
        }

        private Subscription[] GetSubscriptions(ReadOnlySpan<byte> packet, int headerLength)
        {
            if (packet.Length - headerLength < 4) //At least 4 bytes required on payload: MSB, LSB, Topic Filter, Requests QoS
                throw new MqttException("SubscribeFormatter_MissingTopicFilterQosPair");

            var subscriptions = new List<Subscription>();
            var index = headerLength;

            while (index < packet.Length)
            {
                var topicFilter = packet.GetString(index, out index);

                if (!_topicEvaluator.IsValidTopicFilter(topicFilter))
                {
                    var error = string.Format("SubscribeFormatter_InvalidTopicFilter {0}", topicFilter);

                    throw new MqttException(error);
                }

                if (index >= packet.Length)
                    throw new MqttException("SubscribeFormatter_MissingTopicFilterQosPair");

                var requestedQosByte = packet[index];

                if (!Enum.IsDefined(typeof(MqttQos), requestedQosByte))
                    throw new MqttException("Formatter_InvalidQualityOfService");

                subscriptions.Add(new Subscription(topicFilter, (MqttQos)requestedQosByte));
                index++;
            }

            return subscriptions.ToArray();
        }
    }
}
