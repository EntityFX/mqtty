using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal abstract class Formatter<T> : IFormatter
        where T : class, IPacket
    {
        public abstract MqttPacketType PacketType { get; }

        protected abstract T Read(byte[] bytes);

        protected abstract byte[] Write(T packet);

        protected static MqttEncoder Encoding => MqttEncoder.Default;

        protected static string GetAnonymousClientId() =>
            string.Format(
                "anonymous{0}",
                Guid.NewGuid().ToString().Replace("-", string.Empty).Substring(0, 10)
        );

        public IPacket Format(byte[] bytes)
        {
            var actualType = (MqttPacketType)bytes.Byte(0).Bits(4);

            if (PacketType != actualType)
            {
                var error = string.Format("InvalidPacket {0}", typeof(T).Name);

                throw new MqttException(error);
            }

            var packet = Read(bytes);

            return packet;
        }

        public byte[] Format(IPacket packet)
        {
            if (packet.Type != PacketType)
            {
                var error = string.Format("InvalidPacket {0}", typeof(T).Name);

                throw new MqttException(error);
            }

            var packetTyped = packet! as T;
            if (packetTyped == null)
            {
                var error = string.Format("InvalidPacket {0}", typeof(T).Name);

                throw new MqttException(error);
            }

            var bytes = Write(packetTyped);

            return bytes;
        }

        protected void ValidateHeaderFlag(byte[] bytes, Func<MqttPacketType, bool> packetTypePredicate, int expectedFlag)
        {
            var headerFlag = bytes.Byte(0).Bits(5, 4);

            if (packetTypePredicate(PacketType) && headerFlag != expectedFlag)
            {
                var error = string.Format("InvalidHeaderFlag {0} {1} {2}", headerFlag, typeof(T).Name, expectedFlag);

                throw new MqttException(error);
            }
        }
    }


}
