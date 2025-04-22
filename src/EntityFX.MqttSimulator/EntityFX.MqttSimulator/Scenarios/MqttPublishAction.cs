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
        public override async Task ExecuteAsync()
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
                return;
            }

            _ = Task.Run(async () =>
            {
                var sw = new Stopwatch();
                sw.Start();
                while (true)
                {
                    foreach (var item in mqttClients!)
                    {
                        await item.Client.PublishAsync(item.Options.Topic, item.Options.Payload, MqttQos.AtLeastOnce);
                    }

                    if (sw.Elapsed > Config.PublishPeriod)
                    {
                        break;
                    }
                }
            });


            //Context.NetworkGraph!.Refresh();
        }

    }
}
