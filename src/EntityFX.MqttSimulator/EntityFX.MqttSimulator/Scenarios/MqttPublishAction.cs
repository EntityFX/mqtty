using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Network;
using System.Diagnostics;
using System.Linq;

namespace EntityFX.MqttY.Scenarios
{
    record MqttClientPair(MqttPublishActionOptions Options, IMqttClient Client);

    internal class MqttPublishAction : ScenarioAction<NetworkSimulation, MqttPublishOptions>
    {
        public override Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            var mqttClients = Config.Actions.SelectMany(ac =>
            {
                if (!ac.Multi)
                {
                    var mqttClient = Context!.NetworkGraph!.GetNode(ac.ClientName, NodeType.Client) as IMqttClient;

                    return mqttClient != null ? new MqttClientPair[] { new MqttClientPair(ac, mqttClient) } : Enumerable.Empty<MqttClientPair>();
                }

                return Context!.NetworkGraph!.Clients
                    .Where(c => c.Key.Contains(ac.ClientName))
                    .Select(kv => kv.Value as IMqttClient)
                    .OfType<IMqttClient>()
                    .Select(c => new MqttClientPair(ac, c));
            }).ToArray();

            if (mqttClients?.Any() != true)
            {
                return Task.CompletedTask;
            }

            StartPublish(mqttClients);

            return Task.CompletedTask;
        }

        private void StartPublish(MqttClientPair[] mqttClients)
        {
            Task.Run(async () =>
            {
                var sw = new Stopwatch();
                sw.Start();
                var published = 0;
                var failedPublish = 0;
                while (true)
                {
                    foreach (var item in mqttClients!)
                    {
                        var publishResult = await item.Client.PublishAsync(item.Options.Topic, item.Options.Payload, MqttQos.AtLeastOnce);

                        if (publishResult)
                        {
                            published++;
                        }
                        else
                        {
                            failedPublish++;
                        }
                    }

                    if (sw.Elapsed > Config!.PublishPeriod)
                    {
                        Context!.NetworkGraph!.AddCounterValue<long>("TotalPublished", published);
                        Context!.NetworkGraph!.AddCounterValue<long>("FailedPublish", failedPublish);
                        break;
                    }
                }
            });
        }
    }
}
