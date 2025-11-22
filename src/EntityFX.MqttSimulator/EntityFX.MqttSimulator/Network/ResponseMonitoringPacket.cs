using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class ResponseMonitoringPacket
{
    public NetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public NetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public Guid Id { get; set; }
    

    public bool ReceiveIsSet { get; internal set; }

    public bool IsExpired { get; private set; } = false;

    internal long WaitTicks => _waitTicks;
    
    private long _waitTicks = 600000;

    public string Marker { get; set; } = string.Empty;

    internal void ReduceWaitTicks()
    {
        Interlocked.Decrement(ref _waitTicks);

        if (_waitTicks <= 0)
        {
            IsExpired = true;
        }
    }

    public bool WaitIsSet(TimeSpan timeSpan)
    {
        while (!ReceiveIsSet)
        {
            Thread.Sleep(1);
        }
        
        return ReceiveIsSet;
    }
}