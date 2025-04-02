namespace EntityFX.MqttY.Mqtt.Internals.Formatters
{
    internal static class MqttProtocolConsts
    {
        public const int SupportedLevel = 4;

        public const int ClientIdMaxLength = 65535;

        internal const int MaxIntegerLength = 65535;

        internal const int StringPrefixLength = 2;

        internal const int PacketTypeLength = 1;

        internal const string Name = "MQTT";

        internal static readonly int NameLength = Name.Length + StringPrefixLength;
    }


}
