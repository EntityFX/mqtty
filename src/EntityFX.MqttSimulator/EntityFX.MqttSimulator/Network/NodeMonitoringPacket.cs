using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class NodeMonitoringPacket
{
    private long _outgoingWaitTicks;
    
    public NodeMonitoringPacket(long tick, INetworkPacket requestPacket, bool passTillNextTick, bool waitMode)
    {
        RequestPacket = requestPacket;
        WaitMode = waitMode;
        PassTillNextTick = passTillNextTick;
        Tick = tick;
    }

    public INetworkPacket RequestPacket { get; set; }

    public long Tick { get; }

    internal bool PassTillNextTick { get; private set; }

    public long WaitTicks { get => _outgoingWaitTicks; init => _outgoingWaitTicks = value; }
    
    public long SendTick { get; init; }

    public bool Released { get; private set; }
    
    public Guid Id { get; set; }

    public ManualResetEventSlim ResetEventSlim { get; private set; } = new ManualResetEventSlim(false);

    public bool WaitMode { get; }

    internal void ReduceWaitTicks()
    {
        Interlocked.Decrement(ref _outgoingWaitTicks);
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