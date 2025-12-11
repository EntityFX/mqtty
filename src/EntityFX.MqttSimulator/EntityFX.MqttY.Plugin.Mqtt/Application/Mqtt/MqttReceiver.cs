using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Mqtt.Counter;

namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{
    public class MqttReceiver : Application<MqttReceiverConfiguration>
    {
        private IMqttClient? _mqttClient;

        private MqttReceiverCounters _receiverCounter;
        private readonly IClientBuilder _clientBuilder;
        private readonly TicksOptions _ticksOptions;


        public MqttReceiver(IClientBuilder clientBuilder, int index, string name, string address, string protocolType, string specification, 
            TicksOptions ticksOptions,
            MqttReceiverConfiguration? options) 
            : base(index, name, address, protocolType, specification, ticksOptions, options)
        {
            this._ticksOptions = ticksOptions;
            _receiverCounter = new MqttReceiverCounters("Receiver", _ticksOptions.CounterHistoryDepth);
            this._clientBuilder = clientBuilder;
            counters.AddCounter(_receiverCounter);
        }

        public override void Start()
        {
            if (Options?.Server == null)
            {
                return;
            }

            _mqttClient = AddMqttClient(Options.Server);

            base.Start();

            SubscribeListenTopics(_mqttClient, Options.Topics);


        }

        private void SubscribeListenTopics(IMqttClient? mqttClient, string[] listenTopics)
        {

            if (mqttClient == null) return;

            foreach (var listenTopic in listenTopics)
            {
                mqttClient.Subscribe(listenTopic!, MqttQos.AtLeastOnce);
            }
        }

        private IMqttClient? AddMqttClient(string serverOption)
        {
            var nodeName = GetNodeName(Name, serverOption);

            var listenerMqttClient = _clientBuilder.BuildClient<IMqttClient>(0, nodeName!, ProtocolType,
                "mqtt-client",
                    Network!, _ticksOptions);

            if (listenerMqttClient == null)
            {
                return null;
            }

            AddClient(listenerMqttClient);

            try
            {
                listenerMqttClient.Connect(serverOption);
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
            NetworkSimulator!.Monitoring.Push(Guid.NewGuid(), NetworkSimulator.TotalTicks, NetworkLoggerType.Receive,
                $"Mqtt Application {Name} receives message by topic {e.Topic} from broker {e.Broker}", 
                Specification, "MQTT Receiver Application");
            _receiverCounter.Receive();
        }

        private string GetNodeName(string group, string key) => $"{group}{key}";
    }
}
