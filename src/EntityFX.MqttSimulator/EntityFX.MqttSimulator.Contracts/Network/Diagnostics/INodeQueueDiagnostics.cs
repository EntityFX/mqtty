namespace EntityFX.MqttY.Contracts.Network.Diagnostics;

/// <summary>Read-only queue counts; capture never advances model time.</summary>
public interface INodeQueueDiagnostics
{
    NodeQueueSnapshot GetQueueSnapshot();
}
