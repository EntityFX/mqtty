using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

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
        public void DumpMqttPublishTest()
        {
            var connect = new PublishPacket("aaa/bbbb/ccccc/ddddddd/eeeee/ffff", MqttQos.ExactlyOnce, true, true, 77) {
                Payload = new byte[555] };

            var topicEvaluator = new MqttTopicEvaluator(true);

            var mqttDumper = new MqttProtocolDumper(topicEvaluator);

            Console.WriteLine(mqttDumper.Dump(connect));
        }

        [TestMethod]
        public void DumpMqttConnectTest()
        {
            var connect = new ConnectPacket("abcde", true);

            var topicEvaluator = new MqttTopicEvaluator(true);

            var mqttDumper = new MqttProtocolDumper(topicEvaluator);

            Console.WriteLine(mqttDumper.Dump(connect));
        }
    }
}