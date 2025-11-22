using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class OutgoingMonitoringPacket
{
    private long _delayTicks = 1;
    
    public OutgoingMonitoringPacket(NetworkPacket requestPacket)
    {
        RequestPacket = requestPacket;
    }

    public NetworkPacket RequestPacket { get; set; }
    
    public long DelayTicks { get => _delayTicks; init => _delayTicks = value; }
    
    public long SendTick { get; init; }
    
    public Guid Id { get; set; }
    
    internal void ReduceDelayTicks()
    {
        Interlocked.Decrement(ref _delayTicks);
    }
}