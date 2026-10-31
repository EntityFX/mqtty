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

    public long Tick { get; set; }

    internal bool PassTillNextTick { get; set; }

    public long WaitTicks { get => _outgoingWaitTicks; set => _outgoingWaitTicks = value; }
    
    public long SendTick { get; set; }

    public bool Released { get; private set; }
    
    public long Id { get; set; }

    public ManualResetEventSlim ResetEventSlim { get; private set; } = new ManualResetEventSlim(false);

    public bool WaitMode { get; set; }

    internal void ReduceWaitTicks()
    {
        Interlocked.Decrement(ref _outgoingWaitTicks);
    }

    public void Reset(long tick, INetworkPacket requestPacket, bool passTillNextTick, bool waitMode, long waitTicks, long id, long sendTick)
    {
        Tick = tick;
        RequestPacket = requestPacket;
        PassTillNextTick = passTillNextTick;
        WaitMode = waitMode;
        WaitTicks = waitTicks;
        Id = id;
        SendTick = sendTick;
        Released = false;
        _outgoingWaitTicks = waitTicks;
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