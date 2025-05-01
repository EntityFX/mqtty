using System.Diagnostics;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Scenarios;

namespace EntityFX.MqttY.Plugin.Mqtt.Scenarios
{
    record MqttClientPair(MqttPublishActionOptions Options, IMqttClient Client);

    public class MqttPublishAction : ScenarioAction<NetworkSimulation, MqttPublishOptions>
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
                var publishPerTick = 0;
                var lastTick = Context!.NetworkGraph!.TotalTicks;
                while (true)
                {
                    if (Context!.NetworkGraph!.TotalTicks > lastTick)
                    {
                        publishPerTick = 0;
                        lastTick = Context!.NetworkGraph!.TotalTicks;
                    }

                    if (publishPerTick > Config!.PublishTicks)
                    {
                        continue;
                    }

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

                        publishPerTick++;
                    }

                    

                    if (sw.Elapsed > Config!.PublishPeriod)
                    {
                        Context!.NetworkGraph!.AddCounterValue<long>("TotalPublished", published);
                        Context!.NetworkGraph!.AddCounterValue<long>("FailedPublish", failedPublish);
                        Context!.NetworkGraph!.AddCounterValue<long>("PublishPerTick", published / Context!.NetworkGraph!.TotalTicks);
                        break;
                    }
                }
            });
        }
    }
}
