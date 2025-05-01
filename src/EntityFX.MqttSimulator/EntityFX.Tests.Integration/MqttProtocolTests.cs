using EntityFX.MqttY.Helper;
using System.Collections;
using System.Text;
using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttProtocolTests
    {

        //|   Bit    |  7  |  6  |  5  |  4  |  3  |  2  |  1  |   0    |  <-- Fixed Header
        //|----------|-----------------------|--------------------------|
        //| Byte 1   |      MQTT type 3      | dup |    QoS    | retain |
        //|----------|--------------------------------------------------|
        //| Byte 2   |                                                  |
        //|  .       |               Remaining Length                   |
        //|  .       |                                                  |
        //| Byte 5   |                                                  |
        //|----------|--------------------------------------------------|  <-- Variable Header
        //| Byte 6   |                Topic len MSB                     |
        //| Byte 7   |                Topic len LSB                     |
        //|-------------------------------------------------------------|
        //| Byte 8   |                                                  |
        //|   .      |                Topic name                        |
        //| Byte N   |                                                  |
        //|----------|--------------------------------------------------|
        //| Byte N+1 |            Packet Identifier MSB                 |
        //| Byte N+2 |            Packet Identifier LSB                 |
        //|----------|--------------------------------------------------|  <-- Payload
        //| Byte N+3 |                   Payload                        |
        //| Byte N+M |                                                  |
        [TestMethod]
        public void DumpMqttTest()
        {
            var connect = new PublishPacket("aaa/bbbb/ccccc/ddddddd/eeeee/ffff", MqttQos.ExactlyOnce, true, true, 77) {
                Payload = new byte[555] };

            var topicEvaluator = new MqttTopicEvaluator(true);

            var cf = new PublishFormatter(topicEvaluator);
            var packetBytes = cf.Format(connect);

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

            foreach ( var remainingLengthByte in remainingLengthBytes)
            {
                var remainingLengthBit = new BitArray(new byte[] { remainingLengthByte });
                byteIndex++;
                byteIndexStr = $"Byte {byteIndex}".CenterString(10);

                var remainingLengthBitStr = remainingLengthBit.BitsToStringPad(8, 6).CenterString(50);
                dumpMqtt.AppendLine($"|{byteIndexStr}|{remainingLengthBitStr}|");
            }

            dumpMqtt.AppendLine($"|----------|--------------------------------------------------|  <-- Variable Header");

            Console.WriteLine(dumpMqtt.ToString());
        }
    }
}