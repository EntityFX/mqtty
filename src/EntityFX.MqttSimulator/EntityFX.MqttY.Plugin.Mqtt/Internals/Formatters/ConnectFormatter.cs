using System.Text.RegularExpressions;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{



    internal class ConnectFormatter : Formatter<ConnectPacket>
    {
        private static readonly Regex ClientIdRegex =
            new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public override MqttPacketType PacketType { get { return MqttPacketType.Connect; } }

        protected override ConnectPacket Read(byte[] bytes)
        {
            ValidateHeaderFlag(bytes, t => t == MqttPacketType.Connect, 0x00);

            var remainingLengthBytesLength = 0;

            Encoding.DecodeRemainingLength(bytes, out remainingLengthBytesLength);

            var protocolName = bytes.GetString(MqttProtocolConsts.PacketTypeLength + remainingLengthBytesLength);

            if (protocolName != MqttProtocolConsts.Name)
            {
                var error = string.Format("InvalidProtocolName {0}", protocolName);

                throw new MqttException(error);
            }

            var protocolLevelIndex = MqttProtocolConsts.PacketTypeLength + remainingLengthBytesLength + MqttProtocolConsts.NameLength;
            var protocolLevel = bytes.Byte(protocolLevelIndex);

            if (protocolLevel < MqttProtocolConsts.SupportedLevel)
            {
                var error = string.Format("UnsupportedLevel {0}", protocolLevel);

                throw new MqttConnectException(MqttConnectionStatus.UnacceptableProtocolVersion, error);
            }

            var protocolLevelLength = 1;
            var connectFlagsIndex = protocolLevelIndex + protocolLevelLength;
            var connectFlags = bytes.Byte(connectFlagsIndex);

            if (connectFlags.IsSet(0))
                throw new MqttException("InvalidReservedFlag");

            if (connectFlags.Bits(4, 2) == 0x03)
                throw new MqttException("InvalidQualityOfService");

            var willFlag = connectFlags.IsSet(2);
            var willRetain = connectFlags.IsSet(5);

            if (!willFlag && willRetain)
                throw new MqttException("InvalidWillRetainFlag");

            var userNameFlag = connectFlags.IsSet(7);
            var passwordFlag = connectFlags.IsSet(6);

            if (!userNameFlag && passwordFlag)
                throw new MqttException("InvalidPasswordFlag");

            var willQos = (MqttQos)connectFlags.Bits(4, 2);
            var cleanSession = connectFlags.IsSet(1);

            var keepAliveLength = 2;
            var keepAliveBytes = bytes.Bytes(connectFlagsIndex + 1, keepAliveLength);
            var keepAlive = keepAliveBytes.ToUInt16();

            var payloadStartIndex = connectFlagsIndex + keepAliveLength + 1;
            var nextIndex = 0;
            var clientId = bytes.GetString(payloadStartIndex, out nextIndex);

            if (clientId.Length > MqttProtocolConsts.ClientIdMaxLength)
                throw new MqttConnectException(MqttConnectionStatus.IdentifierRejected, "ClientIdMaxLengthExceeded");

            if (!IsValidClientId(clientId))
            {
                var error = string.Format("InvalidClientIdFormat {0}", clientId);

                throw new MqttConnectException(MqttConnectionStatus.IdentifierRejected, error);
            }

            if (string.IsNullOrEmpty(clientId) && !cleanSession)
                throw new MqttConnectException(MqttConnectionStatus.IdentifierRejected, "ClientIdEmptyRequiresCleanSession");

            if (string.IsNullOrEmpty(clientId))
            {
                clientId = GetAnonymousClientId();
            }

            var connect = new ConnectPacket(clientId, cleanSession);

            connect.KeepAlive = keepAlive;

            if (willFlag)
            {
                //var willTopic = bytes.GetString(nextIndex, out int willMessageIndex);
                //var willMessageLengthBytes = bytes.Bytes(willMessageIndex, count: 2);
                //var willMessageLenght = willMessageLengthBytes.ToUInt16();

                //var willMessage = bytes.Bytes(willMessageIndex + 2, willMessageLenght);

                //connect.Will = new MqttLastWill(willTopic, willQos, willRetain, willMessage);
                //nextIndex = willMessageIndex + 2 + willMessageLenght;
            }

            if (userNameFlag)
            {
                var userName = bytes.GetString(nextIndex, out nextIndex);

                connect.UserName = userName;
            }

            if (passwordFlag)
            {
                var password = bytes.GetString(nextIndex);
                connect.Password = password;
            }

            return connect;
        }

        protected override byte[] Write(ConnectPacket packet)
        {
            var userNameFlag = !string.IsNullOrEmpty(packet.UserName);
            var passwordFlag = userNameFlag && !string.IsNullOrEmpty(packet.Password);

            if (packet.ClientId.Length > MqttProtocolConsts.ClientIdMaxLength)
                throw new MqttException("ClientIdMaxLengthExceeded");

            if (!IsValidClientId(packet.ClientId))
            {
                var error = string.Format("InvalidClientIdFormat {0}", packet.ClientId);

                throw new MqttException(error);
            }

            if (!userNameFlag && !string.IsNullOrEmpty(packet.Password))
                throw new MqttException("PasswordNotAllowed");

            var protocolNameLength = Encoding.GetStringByteCount(MqttProtocolConsts.Name);
            var variableHeaderLength = MqttProtocolConsts.StringPrefixLength + protocolNameLength + 1 + 1 + 2;
            var payloadLength = MqttProtocolConsts.StringPrefixLength +
                Encoding.GetStringByteCount(packet.ClientId);

            if (userNameFlag)
            {
                payloadLength += MqttProtocolConsts.StringPrefixLength +
                    Encoding.GetStringByteCount(packet.UserName);
            }

            if (passwordFlag)
            {
                payloadLength += MqttProtocolConsts.StringPrefixLength +
                    Encoding.GetStringByteCount(packet.Password);
            }

            var remainingLength = variableHeaderLength + payloadLength;
            var remainingLengthBytes = MqttEncoder.GetRemainingLengthByteCount(remainingLength);
            var bytes = new byte[1 + remainingLengthBytes + remainingLength];
            var destination = bytes.AsSpan();

            var flags = 0x00;
            var type = Convert.ToInt32(MqttPacketType.Connect) << 4;
            destination[0] = Convert.ToByte(flags | type);
            var written = 1 + MqttEncoder.WriteRemainingLength(
                destination.Slice(1, remainingLengthBytes), remainingLength);

            Encoding.WriteString(destination.Slice(written), MqttProtocolConsts.Name);
            written += MqttProtocolConsts.StringPrefixLength + protocolNameLength;
            destination[written++] = Convert.ToByte(MqttProtocolConsts.SupportedLevel);
            destination[written++] = GetConnectFlags(packet, userNameFlag, passwordFlag);

            MqttEncoder.WriteInteger(destination.Slice(written), packet.KeepAlive);
            written += 2;

            Encoding.WriteString(destination.Slice(written), packet.ClientId);
            written += MqttProtocolConsts.StringPrefixLength +
                Encoding.GetStringByteCount(packet.ClientId);

            if (userNameFlag)
            {
                Encoding.WriteString(destination.Slice(written), packet.UserName);
                written += MqttProtocolConsts.StringPrefixLength +
                    Encoding.GetStringByteCount(packet.UserName);
            }

            if (passwordFlag)
            {
                Encoding.WriteString(destination.Slice(written), packet.Password);
            }

            return bytes;
        }

        private static byte GetConnectFlags(ConnectPacket packet, bool userNameFlag, bool passwordFlag)
        {
            var reserved = 0x00;
            var cleanSession = Convert.ToInt32(packet.CleanSession);
            var willFlag = 0;//Convert.ToInt32(packet.Will != null);
            var willQos = 0;//packet.Will == null ? 0 : Convert.ToInt32(packet.Will.QualityOfService);
            var willRetain = 0;//packet.Will == null ? 0 : Convert.ToInt32(packet.Will.Retain);

            cleanSession <<= 1;
            willFlag <<= 2;
            willQos <<= 3;
            willRetain <<= 5;

            var password = Convert.ToInt32(passwordFlag) << 6;
            var user = Convert.ToInt32(userNameFlag) << 7;

            return Convert.ToByte(reserved | cleanSession | willFlag | willQos | willRetain | password | user);
        }

        bool IsValidClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                return true;

            return ClientIdRegex.IsMatch(clientId);
        }
    }


}
