using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal static class Extensions
    {

        internal static SubscribeReturnCode ToReturnCode(this MqttQos qos)
        {
            var returnCode = default(SubscribeReturnCode);

            switch (qos)
            {
                case MqttQos.AtMostOnce:
                    returnCode = SubscribeReturnCode.MaximumQoS0;
                    break;
                case MqttQos.AtLeastOnce:
                    returnCode = SubscribeReturnCode.MaximumQoS1;
                    break;
                case MqttQos.ExactlyOnce:
                    returnCode = SubscribeReturnCode.MaximumQoS2;
                    break;
            }

            return returnCode;
        }
    }
}
