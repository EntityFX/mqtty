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


        public long Received => _receiverCounter.Received;

        public MqttReceiver(IClientBuilder clientBuilder, int index, string name, string address, string protocolType, string specification,
            TicksOptions ticksOptions,
            MqttReceiverConfiguration? options, bool enableCounters)
            : base(index, name, address, protocolType, specification, ticksOptions, options, enableCounters)
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
        }

        public bool HasListenSubscription(string server, string topic)
        {
            var groupName = $"{Name}{server}";

            if (_mqttClient == null)
            {
                return false;
            }

            var subscribtions = _mqttClient.Subscribtions.GetValueOrDefault(groupName);
            if (subscribtions == null)
            {
                return false;
            }

            var hasSubscriptionToTopic = subscribtions.FirstOrDefault(s => s.TopicFilter == topic);

            return hasSubscriptionToTopic != null;
        }

        public void SubscribeAll()
        {
            SubscribeListenTopics(_mqttClient, Options!.Topics);
        }

        private void SubscribeListenTopics(IMqttClient? mqttClient, string[] listenTopics)
        {

            if (mqttClient == null) return;

            foreach (var listenTopic in listenTopics)
            {
                mqttClient.BeginSubscribe(listenTopic!, MqttQos.AtLeastOnce);
            }
        }

        private IMqttClient? AddMqttClient(string serverOption)
        {
            var nodeName = GetNodeName(Name, serverOption);

            var listenerMqttClient = _clientBuilder.BuildClient<IMqttClient>(NetworkSimulator!.CountNodes + 1, nodeName!, ProtocolType,
                "mqtt-client",
                    Network!, _ticksOptions, NetworkSimulator!.EnableCounters, nodeName);

            if (listenerMqttClient == null)
            {
                return null;
            }

            AddClient(listenerMqttClient);

            try
            {
                listenerMqttClient.BeginConnect(serverOption, false);
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

        public override void Clear()
        {
            if (_mqttClient != null)
            {
                _mqttClient.MessageReceived -= ListenerMqttClient_MessageReceived;
            }

            base.Clear();
        }
    }
}
