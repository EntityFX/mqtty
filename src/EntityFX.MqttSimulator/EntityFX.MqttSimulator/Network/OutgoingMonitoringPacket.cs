using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class OutgoingMonitoringPacket
{
    private long _delayTicks = 1;
    
    public OutgoingMonitoringPacket(INetworkPacket requestPacket, bool waitMode)
    {
        RequestPacket = requestPacket;
        WaitMode = waitMode;
    }

    public INetworkPacket RequestPacket { get; set; }
    
    public long DelayTicks { get => _delayTicks; init => _delayTicks = value; }
    
    public long SendTick { get; init; }

    public bool Released { get; private set; }
    
    public Guid Id { get; set; }

    public ManualResetEventSlim ResetEventSlim { get; private set; } = new ManualResetEventSlim(false);

    public bool WaitMode { get; }

    internal void ReduceDelayTicks()
    {
        Interlocked.Decrement(ref _delayTicks);
    }

    public bool WaitIsReleased(TimeSpan timeSpan)
    {
        var isSet = ResetEventSlim?.Wait(TimeSpan.FromMinutes(1));

        return isSet ?? false;
    }

    public void Release()
    {
        Released = true;
        if (WaitMode)
        {
            ResetEventSlim.Set();
        }
    }
}