using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttReceiver : Application<MqttReceiverConfiguration>
    {
        private IMqttClient? _mqttClient;

        public MqttReceiver(int index, string name, string address, string protocolType, string specification, 
            INetwork network, INetworkGraph networkGraph, MqttReceiverConfiguration? options) 
            : base(index, name, address, protocolType, specification, network, networkGraph, options)
        {
        }

        public override async Task StartAsync()
        {

            _mqttClient = await AddMqttClient(Options?.Server);

            await base.StartAsync();

            await SubscribeListenTopics(_mqttClient, Options.Topics);


        }

        private async Task SubscribeListenTopics(IMqttClient? mqttClient, string[] listenTopics)
        {

            if (mqttClient == null) return;

            foreach (var listenTopic in listenTopics)
            {
                await mqttClient.SubscribeAsync(listenTopic!, MqttQos.AtLeastOnce);
            }
        }

        private async Task<IMqttClient?> AddMqttClient(string serverOption)
        {
            var nodeName = GetNodeName(Name, serverOption);

            var listenerMqttClient = NetworkGraph.BuildClient<IMqttClient>(0, nodeName, ProtocolType,
                "mqtt-client",
                    Network!, null);

            if (listenerMqttClient == null)
            {
                return null;
            }

            AddClient(listenerMqttClient);

            await listenerMqttClient.ConnectAsync(serverOption);


            listenerMqttClient.MessageReceived += ListenerMqttClient_MessageReceived;

            return listenerMqttClient;
        }

        private void ListenerMqttClient_MessageReceived(object? sender, MqttMessage e)
        {
            NetworkGraph.Monitoring.Push(Contracts.Monitoring.MonitoringType.Receive, "mqtt", Specification, 
                $"Mqtt Application {Name} receives message by topic {e.Topic} from broker {e.Broker}");
        }

        private string GetNodeName(string group, string key) => $"{group}_{key}";
    }
}
