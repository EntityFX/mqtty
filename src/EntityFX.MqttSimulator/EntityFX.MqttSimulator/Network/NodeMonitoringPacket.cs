using System.Diagnostics;
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
        // A pooled packet may have been released while waiting in WaitMode.
        // Clear the event so the next waiter cannot observe a stale release.
        if (waitMode)
        {
            ResetEventSlim.Reset();
        }
    }

    public bool WaitIsReleased(TimeSpan timeSpan)
    {
        if (ResetEventSlim == null)
        {
            return false;
        }

        // Bounded spin avoids an immediate kernel transition for short simulation
        // responses; once spins are exhausted we fall back to the blocking Wait
        // with the caller-supplied timeout, preserving the original semantics
        // without monopolizing a CPU core.
        var spinner = new SpinWait();
        while (!ResetEventSlim.IsSet)
        {
            if (spinner.NextSpinWillYield)
            {
                return ResetEventSlim.Wait(timeSpan);
            }

            spinner.SpinOnce();
        }

        return true;
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