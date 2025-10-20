using EntityFX.MqttY.Helper;
using System.Collections;
using System.Text;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Helper
{
    public class MqttProtocolDumper
    {
        readonly IDictionary<MqttPacketType, IFormatter> _formatters;

        public MqttProtocolDumper(IMqttTopicEvaluator mqttTopicEvaluator)
        {
            _formatters = new Dictionary<MqttPacketType, IFormatter>()
            {
                [MqttPacketType.Connect] = new ConnectFormatter(),
                [MqttPacketType.ConnectAck] = new ConnectAckFormatter(),
                [MqttPacketType.Subscribe] = new SubscribeFormatter(mqttTopicEvaluator),
                [MqttPacketType.SubscribeAck] = new SubscribeAckFormatter(),
                [MqttPacketType.Publish] = new PublishFormatter(mqttTopicEvaluator),
                [MqttPacketType.PublishAck] = new FlowPacketFormatter<PublishAckPacket>(
                    MqttPacketType.PublishAck, id => new PublishAckPacket(id)),
                [MqttPacketType.PublishReceived] = new FlowPacketFormatter<PublishReceivedPacket>(
                    MqttPacketType.PublishReceived, id => new PublishReceivedPacket(id)),
                [MqttPacketType.PublishComplete] = new FlowPacketFormatter<PublishCompletePacket>(
                    MqttPacketType.PublishComplete, id => new PublishCompletePacket(id)),
            };
        }


        public string Dump(IPacket packet)
        {
            var cf = _formatters[packet.Type];
            var packetBytes = cf.Format(packet);

            var headerByte = packetBytes.Byte(0);

            var retainFlag = (byte)(headerByte & 0b0000_0001);
            var qos = (byte)((headerByte & 0b0000_0110) >> 1);
            var dupFlag = (byte)((headerByte & 0b0000_1000) >> 3);
            var messageType = (byte)(headerByte >> 4);

            var remainingLengthValue = MqttEncoder.Default.DecodeRemainingLength(packetBytes, out var arrLength, out var remainingLengthBytes);

            var dumpMqtt = new StringBuilder();
            dumpMqtt.AppendLine("|   Bit    |  7  |  6  |  5  |  4  |  3  |  2  |  1  |   0    |  <-- Fixed Header");
            dumpMqtt.AppendLine("|----------|-----------------------|--------------------------|");

            var mqttTypeBit = new BitArray(new byte[] { messageType });
            var qosFlagsBit = new BitArray(new byte[] { qos });

            var mqttTypeStr = mqttTypeBit.BitsToStringPad(4, 6).CenterString(23);
            var dupFlagStr = dupFlag.FlagToStringRep(false).CenterString(5);
            var qosStr = qosFlagsBit.BitsToStringPad(2, 5).CenterString(11);
            var retainStr = dupFlag.FlagToStringRep(false).CenterString(8);

            var byteIndex = 0;
            var byteIndexStr = $"Byte {byteIndex}".CenterString(10);

            dumpMqtt.AppendLine($"|          |{$"MQTT type [0x{messageType:X2}]".CenterString(23)}|" +
                $" dup |{$" Qos [0x{qos:X1}]".CenterString(11)}| retain |");
            dumpMqtt.AppendLine($"|{byteIndexStr}|{mqttTypeStr}|{dupFlagStr}|{qosStr}|{retainStr}|");
            byteIndex++;
            byteIndexStr = $"Byte {byteIndex}".CenterString(10);

            dumpMqtt.AppendLine("|----------|-----------------------|--------------------------|");

            var remainingLengthStr = $"Remaining Length [{remainingLengthBytes.ToBytesHexString()}]".CenterString(50);
            dumpMqtt.AppendLine($"|{byteIndexStr}|{remainingLengthStr}|");

            foreach (var remainingLengthByte in remainingLengthBytes)
            {
                var remainingLengthBit = new BitArray(new byte[] { remainingLengthByte });
                byteIndex++;
                byteIndexStr = $"Byte {byteIndex}".CenterString(10);

                var remainingLengthBitStr = remainingLengthBit.BitsToStringPad(8, 6).CenterString(50);
                dumpMqtt.AppendLine($"|{byteIndexStr}|{remainingLengthBitStr}|");
            }

            dumpMqtt.AppendLine($"|----------|--------------------------------------------------|  <-- Variable Header");

            return dumpMqtt.ToString();
        }
    }
}