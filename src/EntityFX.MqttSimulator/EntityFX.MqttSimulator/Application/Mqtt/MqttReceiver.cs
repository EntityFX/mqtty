using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Counter;
using System.Diagnostics.Metrics;

namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttReceiver : Application<MqttReceiverConfiguration>
    {
        private IMqttClient? _mqttClient;

        private MqttReceiverCounters receiverCounter = new MqttReceiverCounters("Receiver");
        private readonly INetworkSimulatorBuilder networkSimulatorBuilder;
        private readonly TicksOptions _ticksOptions;

        public MqttReceiver(INetworkSimulatorBuilder networkSimulatorBuilder, int index, string name, string address, string protocolType, string specification, 
            INetwork network, INetworkSimulator networkGraph, TicksOptions ticksOptions, MqttReceiverConfiguration? options) 
            : base(index, name, address, protocolType, specification, network, networkGraph, options)
        {
            this.networkSimulatorBuilder = networkSimulatorBuilder;
            this._ticksOptions = ticksOptions;
            counters.AddCounter(receiverCounter);
        }

        public override async Task StartAsync()
        {
            if (Options?.Server == null)
            {
                return;
            }

            _mqttClient = await AddMqttClient(Options.Server);

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

            var listenerMqttClient = networkSimulatorBuilder.BuildClient<IMqttClient>(0, nodeName, ProtocolType,
                "mqtt-client",
                    Network!, null, _ticksOptions, null);

            if (listenerMqttClient == null)
            {
                return null;
            }

            AddClient(listenerMqttClient);

            try
            {
                await listenerMqttClient.ConnectAsync(serverOption);
            }
            catch (Exception)
            {
                return null;
            }


            listenerMqttClient.MessageReceived += ListenerMqttClient_MessageReceived;

            return listenerMqttClient;
        }

        private void ListenerMqttClient_MessageReceived(object? sender, MqttMessage e)
        {
            NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, NetworkLoggerType.Receive,
                $"Mqtt Application {Name} receives message by topic {e.Topic} from broker {e.Broker}", 
                Specification, "MQTT Receiver Application");
            receiverCounter.Receive();
        }

        private string GetNodeName(string group, string key) => $"{group}{key}";
    }
}
