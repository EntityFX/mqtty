namespace EntityFX.MqttY.Contracts.Mqtt;

/// <summary>Opt-in instrumentation. Does not change the broker protocol or aggregate metrics.</summary>
public interface IPublisherMeasurementSource
{
    void TrackPublishers(IEnumerable<string> names);
    BrokerQosMetricsSnapshot GetPublisherMetrics(string name, MqttQos qos);
}
