using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt;
namespace EntityFX.MqttY.Federation;

public sealed class ObservedMqttClient : MqttClient
{
    private readonly TransmissionLedger ledger;
    private readonly int site;
    private readonly int target;
    public ObservedMqttClient(IMqttPacketManager packets, int index, string name, TicksOptions ticks,
        TransmissionLedger ledger, int site, int target)
        : base(packets, index, name, $"mqtt://{name}.site{site}", "mqtt", "mqtt-client", name, ticks, true)
    { this.ledger = ledger; this.site = site; this.target = target; }
    public override bool Publish(string topic, byte[] payload, MqttQos qos, bool retain = false)
    {
        var accepted = base.Publish(topic, payload, qos, retain);
        ledger.Observe(NetworkSimulator!.TotalTicks, Name, site, target, topic, payload, accepted);
        return accepted;
    }
}
