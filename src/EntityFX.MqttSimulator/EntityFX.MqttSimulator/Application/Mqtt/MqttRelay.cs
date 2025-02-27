using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EntityFX.MqttY.Application.Mqtt.MqttRelayConfiguration;

namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttRelay : Application<MqttRelayConfiguration>
    {
        public MqttRelay(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph,
            MqttRelayConfiguration? mqttRelayConfiguration) 
            : base(index, name, address, protocolType, network, networkGraph, mqttRelayConfiguration)
        {
        }

        public override async Task StartAsync()
        {
            await AddMqttClients(Options?.ListenTopics, $"{Name}_listen");
            await AddMqttClients(Options?.RelayTopics, $"{Name}_relay");

            await base.StartAsync();
        }

        private async Task AddMqttClients(Dictionary<string, MqttRelayConfigurationItem>? serverTopics, string group)
        {
            if ((serverTopics?.Any()) != true)
            {
                return;
            }

            foreach (var listenServer in serverTopics!)
            {
                var listenerMqttClient = NetworkGraph.BuildClient<IMqttClient>(0, GetNodeName(listenServer.Key), ProtocolType, Network!, group, serverTopics.Count);
                if (listenerMqttClient == null)
                {
                    break;
                }

                AddClient(listenerMqttClient);

                var result = await listenerMqttClient.ConnectAsync(listenServer.Value.Server);
            }
        }

        private string GetNodeName(string key) => $"{Name}_{key}";
    }
}
