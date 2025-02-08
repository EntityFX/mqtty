using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker
    {
        public override NodeType NodeType => NodeType.Server;

        public MqttBroker(string name, string address, INetwork network, INetworkGraph networkGraph)
            : base(name, address, "mqtt", network, networkGraph)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
        }

        private void MqttBroker_PacketReceived(object? sender, Packet e)
        {

        }
    }
}
