﻿namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{
    public enum SubscribeReturnCode : byte
    {
        MaximumQoS0 = 0x00,
        MaximumQoS1 = 0x01,
        MaximumQoS2 = 0x02,
        Failure = 0x80
    }
}
