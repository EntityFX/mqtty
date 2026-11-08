using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Data-oriented node transport slot. It replaces the pooled wrapper class:
/// the logical packet reference is kept (identity-based tests still work), and
/// the wait counter advances once per Refresh, not once per graph tick.
/// </summary>
internal struct NodePacketSlot
{
    public INetworkPacket Packet;
    public long CreatedTick;
    public long WaitTicks;
    public bool PassTillNextTick;
    public long SendTick;
    public long Id;
    public bool Released;
    public ManualResetEventSlim? WaitEvent;

    public NodePacketSlot(long tick, INetworkPacket packet, bool passTillNextTick,
        bool waitMode, long waitTicks, long id)
    {
        Packet = packet;
        CreatedTick = tick;
        WaitTicks = waitTicks;
        PassTillNextTick = passTillNextTick;
        SendTick = tick;
        Id = id;
        Released = false;
        WaitEvent = waitMode ? new ManualResetEventSlim(false) : null;
    }

    public void Release()
    {
        Released = true;
        WaitEvent?.Set();
    }

    public bool WaitIsReleased(TimeSpan timeout)
    {
        if (WaitEvent == null)
        {
            return false;
        }

        var spinner = new SpinWait();
        while (!WaitEvent.IsSet)
        {
            if (spinner.NextSpinWillYield)
            {
                return WaitEvent.Wait(timeout);
            }

            spinner.SpinOnce();
        }

        return true;
    }
}
