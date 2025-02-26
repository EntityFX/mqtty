using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttRelay : Application<MqttRelayConfiguration>
    {
        public MqttRelay(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph,
            MqttRelayConfiguration? mqttRelayConfiguration) 
            : base(index, name, address, protocolType, network, networkGraph, mqttRelayConfiguration)
        {
        }
    }
}
