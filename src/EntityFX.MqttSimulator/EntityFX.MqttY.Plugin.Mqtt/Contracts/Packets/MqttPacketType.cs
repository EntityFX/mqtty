using System.ComponentModel;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
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
        Connect = 0x01,
        /// <summary>
        /// MQTT CONNACK packet
        /// </summary>
        [Description("CONNACK")]
        ConnectAck = 0x02,
        /// <summary>
        /// MQTT PUBLISH packet
        /// </summary>
        [Description("PUBLISH")]
        Publish = 0x03,
        /// <summary>
        /// MQTT PUBACK packet
        /// </summary>
        [Description("PUBACK")]
        PublishAck = 0x04,
        /// <summary>
        /// MQTT PUBREC packet
        /// </summary>
        [Description("PUBREC")]
        PublishReceived = 0x05,
        /// <summary>
        /// MQTT PUBREL packet
        /// </summary>
        [Description("PUBREL")]
        PublishRelease = 0x06,
        /// <summary>
        /// MQTT PUBCOMP packet
        /// </summary>
        [Description("PUBCOMP")]
        PublishComplete = 0x07,
        /// <summary>
        /// MQTT SUBSCRIBE packet
        /// </summary>
        [Description("SUBSCRIBE")]
        Subscribe = 0x08,
        /// <summary>
        /// MQTT SUBACK packet
        /// </summary>
        [Description("SUBACK")]
        SubscribeAck = 0x09,
        /// <summary>
        /// MQTT UNSUBSCRIBE packet
        /// </summary>
        [Description("UNSUBSCRIBE")]
        Unsubscribe = 0x0A,
        /// <summary>
        /// MQTT UNSUBACK packet
        /// </summary>
        [Description("UNSUBACK")]
        UnsubscribeAck = 0x0B,
        /// <summary>
        /// MQTT PINGREQ packet
        /// </summary>
        [Description("PINGREQ")]
        PingRequest = 0x0C,
        /// <summary>
        /// MQTT PINGRESP packet
        /// </summary>
        [Description("PINGRESP")]
        PingResponse = 0x0D,
        /// <summary>
        /// MQTT DISCONNECT packet
        /// </summary>
        [Description("DISCONNECT")]
        Disconnect = 0x0E,
    }
}
