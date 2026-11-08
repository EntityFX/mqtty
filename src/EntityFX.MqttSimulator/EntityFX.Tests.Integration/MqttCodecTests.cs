using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Protocol)]
    public class MqttCodecTests
    {
        private static PublishFormatter Formatter() => new(new MqttTopicEvaluator(true));

        [TestMethod]
        public void PublishWrite_ProducesExactQos1Layout_AndRoundTrips()
        {
            var formatter = Formatter();
            var packet = new PublishPacket("a/b", MqttQos.AtLeastOnce, retain: false,
                duplicated: false, packetId: 258)
            {
                Payload = new byte[] { 1, 2, 3 }
            };

            var bytes = formatter.Format(packet);

            CollectionAssert.AreEqual(new byte[]
            {
                0x32, 0x0A,
                0x00, 0x03, (byte)'a', (byte)'/', (byte)'b',
                0x01, 0x02,
                0x01, 0x02, 0x03
            }, bytes);

            var decoded = (PublishPacket)formatter.Format(bytes);
            Assert.AreEqual("a/b", decoded.Topic);
            Assert.AreEqual(MqttQos.AtLeastOnce, decoded.QualityOfService);
            Assert.AreEqual((ushort)258, decoded.PacketId);
            CollectionAssert.AreEqual(packet.Payload, decoded.Payload);
        }

        [TestMethod]
        public void PublishWrite_Qos0HasNoPacketId()
        {
            var formatter = Formatter();
            var packet = new PublishPacket("x", MqttQos.AtMostOnce, retain: true,
                duplicated: false, packetId: null);

            var bytes = formatter.Format(packet);

            CollectionAssert.AreEqual(new byte[]
            {
                0x31, 0x03, 0x00, 0x01, (byte)'x'
            }, bytes);

            var decoded = (PublishPacket)formatter.Format(bytes);
            Assert.IsNull(decoded.PacketId);
            Assert.IsTrue(decoded.Retain);
        }

        [TestMethod]
        public void PublishWrite_UsesMultiByteRemainingLength()
        {
            var formatter = Formatter();
            var packet = new PublishPacket("a", MqttQos.AtMostOnce, retain: false,
                duplicated: false, packetId: null)
            {
                Payload = new byte[200]
            };

            var bytes = formatter.Format(packet);

            Assert.AreEqual(0x30, bytes[0]);
            Assert.AreEqual(0xCB, bytes[1]);
            Assert.AreEqual(0x01, bytes[2]);

            var decoded = (PublishPacket)formatter.Format(bytes);
            Assert.AreEqual(200, decoded.Payload.Length);
        }

        [TestMethod]
        public void PublishWrite_RoundTripsUtf8TopicWithCorrectByteLength()
        {
            var formatter = Formatter();
            const string topic = "тест/1";
            var packet = new PublishPacket(topic, MqttQos.ExactlyOnce, retain: false,
                duplicated: true, packetId: 7)
            {
                Payload = new byte[] { 9, 8, 7 }
            };

            var bytes = formatter.Format(packet);
            var decoded = (PublishPacket)formatter.Format(bytes);

            Assert.AreEqual(topic, decoded.Topic);
            Assert.AreEqual((ushort)7, decoded.PacketId);
            CollectionAssert.AreEqual(packet.Payload, decoded.Payload);
        }

        [TestMethod]
        public void Encoder_WritesBigEndianIntegersAndRemainingLength()
        {
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x02 },
                MqttEncoder.Default.EncodeInteger((ushort)258));
            CollectionAssert.AreEqual(new byte[] { 0x7F },
                MqttEncoder.Default.EncodeRemainingLength(127));
            CollectionAssert.AreEqual(new byte[] { 0x80, 0x01 },
                MqttEncoder.Default.EncodeRemainingLength(128));
            CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0x7F },
                MqttEncoder.Default.EncodeRemainingLength(2_097_151));

            var decoded = MqttEncoder.Default.DecodeRemainingLength(
                new byte[] { 0x30, 0xFF, 0xFF, 0x7F }, out var lengthBytes);
            Assert.AreEqual(2_097_151, decoded);
            Assert.AreEqual(3, lengthBytes);
        }

        [TestMethod]
        public void ByteExtensions_AreBoundsSafeAndBigEndian()
        {
            var bytes = new byte[] { 1, 2 };

            Assert.AreEqual((byte)1, bytes.Byte(0));
            Assert.AreEqual((byte)0, bytes.Byte(10));
            CollectionAssert.AreEqual(new byte[] { 2 }, bytes.Bytes(1));
            CollectionAssert.AreEqual(Array.Empty<byte>(), bytes.Bytes(10));
            CollectionAssert.AreEqual(Array.Empty<byte>(), bytes.Bytes(1, 0));
            Assert.AreEqual((ushort)258, new byte[] { 0x01, 0x02 }.ToUInt16());
        }

        [TestMethod]
        public void ConnectFormatter_ValidatesClientIdWithoutCompilingRegexPerCall()
        {
            var formatter = new ConnectFormatter();
            var valid = new ConnectPacket("client_-123", cleanSession: true);

            var bytes = formatter.Format(valid);
            var decoded = (ConnectPacket)formatter.Format(bytes);

            Assert.AreEqual("client_-123", decoded.ClientId);
            Assert.ThrowsException<MqttException>(() =>
                formatter.Format(new ConnectPacket("invalid client!", cleanSession: true)));
        }

        [TestMethod]
        public void TopicEvaluator_RepeatedMatchesStayCorrectAfterCaching()
        {
            var evaluator = new MqttTopicEvaluator(true);
            for (var i = 0; i < 100; i++)
            {
                Assert.IsTrue(evaluator.Matches("sensors/room1/temp", "sensors/+/temp"));
                Assert.IsFalse(evaluator.Matches("$SYS/broker/uptime", "#"));
                Assert.IsFalse(evaluator.IsValidTopicFilter("sport/#/ranking"));
                Assert.IsTrue(evaluator.IsValidTopicName("sensors/room1/temp"));
            }
        }
    }
}
