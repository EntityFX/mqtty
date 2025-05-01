using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Counter;

namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{
    public class MqttReceiver : Application<MqttReceiverConfiguration>
    {
        private IMqttClient? _mqttClient;

        private MqttReceiverCounters _receiverCounter = new MqttReceiverCounters("Receiver");
        private readonly INetworkSimulatorBuilder _networkSimulatorBuilder;
        private readonly TicksOptions _ticksOptions;
        private readonly NetworkTypeOption _networkTypeOption;

        public MqttReceiver(INetworkSimulatorBuilder networkSimulatorBuilder, int index, string name, string address, string protocolType, string specification, 
            INetwork network, INetworkSimulator networkGraph, TicksOptions ticksOptions,
            NetworkTypeOption networkTypeOption, MqttReceiverConfiguration? options) 
            : base(index, name, address, protocolType, specification, network, networkGraph, options)
        {
            this._networkSimulatorBuilder = networkSimulatorBuilder;
            this._ticksOptions = ticksOptions;
            this._networkTypeOption = networkTypeOption;
            counters.AddCounter(_receiverCounter);
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

            var listenerMqttClient = _networkSimulatorBuilder.BuildClient<IMqttClient>(0, nodeName, ProtocolType,
                "mqtt-client",
                    Network!, _networkTypeOption, _ticksOptions, null);

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
            _receiverCounter.Receive();
        }

        private string GetNodeName(string group, string key) => $"{group}{key}";
    }
}
