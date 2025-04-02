using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using System;

namespace EntityFX.MqttY.Mqtt.Internals.Formatters
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

        public async Task<IPacket> FormatAsync(byte[] bytes)
        {
            var actualType = (MqttPacketType)bytes.Byte(0).Bits(4);

            if (PacketType != actualType)
            {
                var error = string.Format("InvalidPacket {0}", typeof(T).Name);

                throw new MqttException(error);
            }

            var packet = await Task.Run(() => Read(bytes))
                .ConfigureAwait(continueOnCapturedContext: false);

            return packet;
        }

        public async Task<byte[]> FormatAsync(IPacket packet)
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

            var bytes = await Task.Run(() => Write(packetTyped))
                .ConfigureAwait(continueOnCapturedContext: false);

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
