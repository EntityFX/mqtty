using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using static EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt.MqttRelayConfiguration;

namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{
    public class MqttRelay : Application<MqttRelayConfiguration>
    {
        private readonly Dictionary<string, IMqttClient> _listenClients = new();
        private readonly IClientBuilder _clientBuilder;
        private readonly IMqttTopicEvaluator _mqttTopicEvaluator;
        private readonly TicksOptions _ticksOptions;

        public MqttRelay(int index, string name, string address, string protocolType, string specification,
            IClientBuilder clientBuilder,
            IMqttTopicEvaluator mqttTopicEvaluator, TicksOptions ticksOptions,
            MqttRelayConfiguration? mqttRelayConfiguration, bool enableCounters) 
            : base(index, name, address, protocolType, specification, ticksOptions, mqttRelayConfiguration, enableCounters)
        {
            _clientBuilder = clientBuilder;
            _mqttTopicEvaluator = mqttTopicEvaluator;
            _ticksOptions = ticksOptions;
        }

        public override void Start()
        {
            AddMqttClients(Options?.ListenTopics.ToDictionary(kv => kv.Key, 
                    kv => kv.Value.Server), $"{Name}listen");
            AddMqttClients(Options?.RelayTopics.ToDictionary(kv => kv.Key, 
                    kv => kv.Value.Server), $"{Name}relay");

            base.Start();
        }


        public bool HasListenSubscription(string listenServer, string server, string topic)
        {
            var groupName = $"{Name}listen";
            var nodeName = GetNodeName(groupName, listenServer);
            if (nodeName == null)
            {
                return false;
            }

            var mqttClient = _listenClients.GetValueOrDefault(nodeName);
            if (mqttClient == null)
            {
                return false;
            }

            var clientId = $"{groupName}{listenServer}";
            var subscribtions = mqttClient.Subscribtions.GetValueOrDefault(clientId);
            if (subscribtions == null)
            {
                return false;
            }

            var hasSubscriptionToTopic = subscribtions.FirstOrDefault(s => s.TopicFilter == topic);

            return hasSubscriptionToTopic != null;
        }

        public void SubscribeAll()
        {
            SubscribeListenTopics(Options?.ListenTopics, $"{Name}listen");
        }

        private void SubscribeListenTopics(Dictionary<string, MqttListenConfigurationItem>? subscribeOptions, string groupName)
        {
            if ((subscribeOptions?.Any()) != true)
            {
                return;
            }

            foreach (var listenServer in subscribeOptions!)
            {
                var nodeName = GetNodeName(groupName, listenServer.Key);
                var mqttClient = _listenClients.GetValueOrDefault(nodeName);

                if (mqttClient == null) continue;

                foreach (var listenTopics in listenServer.Value.Topics)
                {
                    mqttClient.BeginSubscribe(listenTopics!, MqttQos.AtLeastOnce);
                }
            }
        }

        private void AddMqttClients(Dictionary<string, string>? serverTopics, string group)
        {
            if ((serverTopics?.Any()) != true)
            {
                return;
            }

            foreach (var listenServer in serverTopics!)
            {
                var nodeName = GetNodeName(group, listenServer.Key);
                var listenerMqttClient = _clientBuilder.BuildClient<IMqttClient>(NetworkSimulator!.CountNodes+1, nodeName, ProtocolType,
                    "mqtt-client",
                    Network!, _ticksOptions, NetworkSimulator!.EnableCounters, group, serverTopics.Count);
                if (listenerMqttClient == null)
                {
                    break;
                }

                AddClient(listenerMqttClient);
                _listenClients.Add(listenerMqttClient.Name, listenerMqttClient);
                listenerMqttClient.BeginConnect(listenServer.Value, false);

                listenerMqttClient.MessageReceived += ListenerMqttClient_MessageReceived;
            }
        }

        private void ListenerMqttClient_MessageReceived(object? sender, MqttMessage e)
        {
            var mqttClient = sender as IMqttClient;
            if (mqttClient == null) return;

            if (mqttClient.Server != e.Broker) return;

            PublishToRelayed(e, Options?.RelayTopics, $"{Name}relay");
        }

        private void PublishToRelayed(MqttMessage mqttMessage, Dictionary<string, MqttRelayConfigurationItem>? relayServers, string group)
        {
            var listenRelayOption = Options?.ListenTopics?.Where(
                lt => lt.Value.Topics.Any(ltv => _mqttTopicEvaluator.Matches(mqttMessage.Topic, ltv)) 
                && lt.Value.Server == mqttMessage.Broker).FirstOrDefault();

            if (listenRelayOption == null) return;

            var listenNodeKey = listenRelayOption!.Value.Key;

            var redirectRoute = Options?.RouteMap.GetValueOrDefault(listenNodeKey);

            if (redirectRoute == null) return;

            foreach (var redirectRouteItem in redirectRoute)
            {
                var relayTopics = Options?.RelayTopics.GetValueOrDefault(redirectRouteItem);

                if (relayTopics == null) continue;

                var relayTopic = mqttMessage.Topic;

                if (relayTopics.ReplaceRelaySegment)
                {
                    var splitSegmentsExceptRelay = relayTopic.Split('/').Skip(1);
                    relayTopic = string.Join("/", splitSegmentsExceptRelay);
                    relayTopic = $"{relayTopics.TopicPrefix}{relayTopic}";
                } else
                {
                    relayTopic = $"{relayTopics.TopicPrefix}{mqttMessage.Topic}";
                }

                if (!_mqttTopicEvaluator.IsValidTopicName(relayTopic))
                {
                    continue;
                }

                var nodeName = GetNodeName(group, redirectRouteItem);
                var relayMqttClient = _listenClients.GetValueOrDefault(nodeName);

                if (relayMqttClient == null) continue;

                relayMqttClient.Publish(relayTopic, mqttMessage.Payload, mqttMessage.Qos);
            }
        }

        private string GetNodeName(string group, string key) => $"{group}{key}";

        public override void Clear()
        {
            if (_listenClients != null)
            {
                foreach (var listenClient in _listenClients.Values)
                {
                    listenClient.MessageReceived -= ListenerMqttClient_MessageReceived;
                }

                _listenClients.Clear();
            }

            base.Clear();
        }
    }
}
