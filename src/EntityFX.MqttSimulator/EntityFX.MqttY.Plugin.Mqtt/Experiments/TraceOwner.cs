namespace EntityFX.MqttY.Plugin.Mqtt.Experiments;

/// <summary>Owns an optional trace created after the simulation graph is constructed.</summary>
internal sealed class TraceOwner : IDisposable
{
    public Tracing.PublicationCohortTrace? Trace { get; set; }
    public void Dispose() { Trace?.Dispose(); Trace = null; }
}
