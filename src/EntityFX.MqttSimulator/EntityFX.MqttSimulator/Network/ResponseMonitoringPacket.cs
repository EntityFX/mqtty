using System.Diagnostics;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class ResponseMonitoringPacket
{
    public INetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public INetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public long Id { get; set; }

    public bool WaitMode { get; }
    

    public bool ReceiveIsSet { get; private set; }

    private readonly long _createdRefresh;
    internal bool HasExpired(long currentRefresh) => currentRefresh - _createdRefresh >= 600000;

    public string Marker { get; set; } = string.Empty;
    public ManualResetEventSlim ResetEventSlim { get; private set; } = new ManualResetEventSlim(false);

    public ResponseMonitoringPacket(bool waitMode, long createdRefresh)
    {
        WaitMode = waitMode;
        _createdRefresh = createdRefresh;
    }

    public bool WaitIsSet(TimeSpan timeSpan)
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

    public void Receive()
    {
        ReceiveIsSet = true;
        if (WaitMode)
        {
            ResetEventSlim.Set();
        }
    }
}
