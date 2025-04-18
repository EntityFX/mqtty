﻿using EntityFX.MqttY.Contracts.Mqtt;
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
        private Dictionary<string, IMqttClient> _listenClients = new Dictionary<string, IMqttClient>();
        private readonly INetworkSimulatorBuilder networkSimulatorBuilder;
        private readonly IMqttTopicEvaluator mqttTopicEvaluator;

        public MqttRelay(int index, string name, string address, string protocolType, string specification,
            INetwork network, INetworkSimulatorBuilder networkSimulatorBuilder,
            IMqttTopicEvaluator mqttTopicEvaluator,
            MqttRelayConfiguration? mqttRelayConfiguration) 
            : base(index, name, address, protocolType, specification, network, networkSimulatorBuilder.NetworkSimulator, mqttRelayConfiguration)
        {
            this.networkSimulatorBuilder = networkSimulatorBuilder;
            this.mqttTopicEvaluator = mqttTopicEvaluator;
        }

        public override async Task StartAsync()
        {
            await AddMqttClients(Options?.ListenTopics.ToDictionary(kv => kv.Key, kv => kv.Value.Server), $"{Name}listen");
            await AddMqttClients(Options?.RelayTopics.ToDictionary(kv => kv.Key, kv => kv.Value.Server), $"{Name}relay");

            await base.StartAsync();

            await SubscribeListenTopics(Options?.ListenTopics, $"{Name}listen");


        }

        private async Task SubscribeListenTopics(Dictionary<string, MqttListenConfigurationItem>? subscribeOptions, string groupName)
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
                    await mqttClient.SubscribeAsync(listenTopics!, MqttQos.AtLeastOnce);
                }
            }
        }

        private async Task AddMqttClients(Dictionary<string, string>? serverTopics, string group)
        {
            if ((serverTopics?.Any()) != true)
            {
                return;
            }

            foreach (var listenServer in serverTopics!)
            {
                var nodeName = GetNodeName(group, listenServer.Key);
                var listenerMqttClient = networkSimulatorBuilder.BuildClient<IMqttClient>(0, nodeName, ProtocolType,
                    "mqtt-client",
                    Network!, group, serverTopics.Count);
                if (listenerMqttClient == null)
                {
                    break;
                }

                AddClient(listenerMqttClient);
                _listenClients.Add(listenerMqttClient.Name, listenerMqttClient);
                var result = await listenerMqttClient.ConnectAsync(listenServer.Value);

                listenerMqttClient.MessageReceived += ListenerMqttClient_MessageReceived;
            }
        }

        private void ListenerMqttClient_MessageReceived(object? sender, MqttMessage e)
        {
            var mqttClient = sender as IMqttClient;
            if (mqttClient == null) return;

            if (mqttClient.Server != e.Broker) return;

            PublishToRelayed(e, Options?.RelayTopics, $"{Name}relay").Wait();
        }

        private async Task PublishToRelayed(MqttMessage mqttMessage, Dictionary<string, MqttRelayConfigurationItem>? relayServers, string group)
        {
            var listenRelayOption = Options?.ListenTopics?.Where(
                lt => lt.Value.Topics.Any(ltv => mqttTopicEvaluator.Matches(mqttMessage.Topic, ltv)) 
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

                if (!mqttTopicEvaluator.IsValidTopicName(relayTopic))
                {
                    continue;
                }

                var nodeName = GetNodeName(group, redirectRouteItem);
                var relayMqttClient = _listenClients.GetValueOrDefault(nodeName);

                if (relayMqttClient == null) continue;

                await relayMqttClient.PublishAsync(relayTopic, mqttMessage.Payload, mqttMessage.Qos);

            }
        }

        private string GetNodeName(string group, string key) => $"{group}{key}";
    }
}
