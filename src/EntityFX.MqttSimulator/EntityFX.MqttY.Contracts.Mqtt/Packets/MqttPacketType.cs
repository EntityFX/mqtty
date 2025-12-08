using System.ComponentModel;

namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    /// <summary>
    /// Represents one of the possible MQTT packet types
    /// </summary>
    public enum MqttPacketType : byte
    {
        /// <summary>
        /// MQTT CONNECT packet
        /// </summary>
        [Description("CONNECT")]
        [Category("CN")]
        Connect = 0x01,
        /// <summary>
        /// MQTT CONNACK packet
        /// </summary>
        [Description("CONNACK")]
        [Category("CA")]
        ConnectAck = 0x02,
        /// <summary>
        /// MQTT PUBLISH packet
        /// </summary>
        [Description("PUBLISH")]
        [Category("PB")]
        Publish = 0x03,
        /// <summary>
        /// MQTT PUBACK packet
        /// </summary>
        [Description("PUBACK")]
        [Category("PA")]
        PublishAck = 0x04,
        /// <summary>
        /// MQTT PUBREC packet
        /// </summary>
        [Description("PUBREC")]
        [Category("PR")]
        PublishReceived = 0x05,
        /// <summary>
        /// MQTT PUBREL packet
        /// </summary>
        [Description("PUBREL")]
        [Category("PL")]
        PublishRelease = 0x06,
        /// <summary>
        /// MQTT PUBCOMP packet
        /// </summary>
        [Description("PUBCOMP")]
        [Category("PC")]
        PublishComplete = 0x07,
        /// <summary>
        /// MQTT SUBSCRIBE packet
        /// </summary>
        [Description("SUBSCRIBE")]
        [Category("SB")]
        Subscribe = 0x08,
        /// <summary>
        /// MQTT SUBACK packet
        /// </summary>
        [Description("SUBACK")]
        [Category("SA")]
        SubscribeAck = 0x09,
        /// <summary>
        /// MQTT UNSUBSCRIBE packet
        /// </summary>
        [Description("UNSUBSCRIBE")]
        [Category("US")]
        Unsubscribe = 0x0A,
        /// <summary>
        /// MQTT UNSUBACK packet
        /// </summary>
        [Description("UNSUBACK")]
        [Category("UA")]
        UnsubscribeAck = 0x0B,
        /// <summary>
        /// MQTT PINGREQ packet
        /// </summary>
        [Description("PINGREQ")]
        [Category("PQ")]
        PingRequest = 0x0C,
        /// <summary>
        /// MQTT PINGRESP packet
        /// </summary>
        [Description("PINGRESP")]
        [Category("PP")]
        PingResponse = 0x0D,
        /// <summary>
        /// MQTT DISCONNECT packet
        /// </summary>
        [Description("DISCONNECT")]
        [Category("DC")]
        Disconnect = 0x0E,
    }
}
