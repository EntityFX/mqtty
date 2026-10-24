namespace EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

public interface IMqttPendingDiagnostics
{
    /// <summary>Exact totals with bounded details. No state or model time is changed.</summary>
    MqttPendingSnapshot GetPendingSnapshot(int detailLimit = 32, int sessionLimit = 64);
}
