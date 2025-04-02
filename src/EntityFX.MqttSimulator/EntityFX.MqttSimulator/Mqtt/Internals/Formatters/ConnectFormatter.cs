using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Mqtt;
using System.Text.RegularExpressions;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Mqtt.Internals.Formatters
{



    internal class ConnectFormatter : Formatter<ConnectPacket>
    {
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
            var bytes = new List<byte>();

            var variableHeader = GetVariableHeader(packet);
            var payload = GetPayload(packet);
            var remainingLength = Encoding.EncodeRemainingLength(variableHeader.Length + payload.Length);
            var fixedHeader = GetFixedHeader(remainingLength);

            bytes.AddRange(fixedHeader);
            bytes.AddRange(variableHeader);
            bytes.AddRange(payload);

            return bytes.ToArray();
        }

        byte[] GetFixedHeader(byte[] remainingLength)
        {
            var fixedHeader = new List<byte>();

            var flags = 0x00;
            var type = Convert.ToInt32(MqttPacketType.Connect) << 4;

            var fixedHeaderByte1 = Convert.ToByte(flags | type);

            fixedHeader.Add(fixedHeaderByte1);
            fixedHeader.AddRange(remainingLength);

            return fixedHeader.ToArray();
        }

        byte[] GetVariableHeader(ConnectPacket packet)
        {
            var variableHeader = new List<byte>();

            var protocolNameBytes = Encoding.EncodeString(MqttProtocolConsts.Name);
            var protocolLevelByte = Convert.ToByte(MqttProtocolConsts.SupportedLevel);

            var reserved = 0x00;
            var cleanSession = Convert.ToInt32(packet.CleanSession);
            var willFlag = 0;//Convert.ToInt32(packet.Will != null);
            var willQos = 0;//packet.Will == null ? 0 : Convert.ToInt32(packet.Will.QualityOfService);
            var willRetain = 0;//packet.Will == null ? 0 : Convert.ToInt32(packet.Will.Retain);
            var userNameFlag = Convert.ToInt32(!string.IsNullOrEmpty(packet.UserName));
            var passwordFlag = userNameFlag == 1 ? Convert.ToInt32(!string.IsNullOrEmpty(packet.Password)) : 0;

            if (userNameFlag == 0 && passwordFlag == 1)
                throw new MqttException("InvalidPasswordFlag");

            cleanSession <<= 1;
            willFlag <<= 2;
            willQos <<= 3;
            willRetain <<= 5;
            passwordFlag <<= 6;
            userNameFlag <<= 7;

            var connectFlagsByte = Convert.ToByte(reserved | cleanSession | willFlag | willQos | willRetain | passwordFlag | userNameFlag);
            var keepAliveBytes = Encoding.EncodeInteger(packet.KeepAlive);

            variableHeader.AddRange(protocolNameBytes);
            variableHeader.Add(protocolLevelByte);
            variableHeader.Add(connectFlagsByte);
            variableHeader.Add(keepAliveBytes[keepAliveBytes.Length - 2]);
            variableHeader.Add(keepAliveBytes[keepAliveBytes.Length - 1]);

            return variableHeader.ToArray();
        }

        byte[] GetPayload(ConnectPacket packet)
        {
            if (packet.ClientId.Length > MqttProtocolConsts.ClientIdMaxLength)
                throw new MqttException("ClientIdMaxLengthExceeded");

            if (!IsValidClientId(packet.ClientId))
            {
                var error = string.Format("InvalidClientIdFormat {0}", packet.ClientId);

                throw new MqttException(error);
            }

            var payload = new List<byte>();

            var clientIdBytes = Encoding.EncodeString(packet.ClientId);

            payload.AddRange(clientIdBytes);


            if (string.IsNullOrEmpty(packet.UserName) && !string.IsNullOrEmpty(packet.Password))
                throw new MqttException("PasswordNotAllowed");

            if (!string.IsNullOrEmpty(packet.UserName))
            {
                var userNameBytes = Encoding.EncodeString(packet.UserName);

                payload.AddRange(userNameBytes);
            }

            if (!string.IsNullOrEmpty(packet.Password))
            {
                var passwordBytes = Encoding.EncodeString(packet.Password);

                payload.AddRange(passwordBytes);
            }

            return payload.ToArray();
        }

        bool IsValidClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                return true;

            var regex = new Regex("^[a-zA-Z0-9]+$");

            return regex.IsMatch(clientId);
        }
    }


}
