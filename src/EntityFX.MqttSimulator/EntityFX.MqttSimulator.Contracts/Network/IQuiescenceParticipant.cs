namespace EntityFX.MqttY.Contracts.Network;

/// <summary>Contributes pending work (for example processing or protocol handshakes) to simulation draining.</summary>
public interface IQuiescenceParticipant
{
    bool IsQuiescent { get; }
}
