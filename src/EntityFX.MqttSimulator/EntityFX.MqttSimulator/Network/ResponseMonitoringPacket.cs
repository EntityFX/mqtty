using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

internal class ResponseMonitoringPacket
{
    public INetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public INetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public Guid Id { get; set; }

    public bool WaitMode { get; }
    

    public bool ReceiveIsSet { get; private set; }

    public bool IsExpired { get; private set; } = false;

    internal long WaitTicks => _waitTicks;
    
    private long _waitTicks = 600000;

    public string Marker { get; set; } = string.Empty;
    public ManualResetEventSlim ResetEventSlim { get; private set; } = new ManualResetEventSlim(false);

    public ResponseMonitoringPacket(bool waitMode)
    {
        WaitMode = waitMode;
    }

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
        var isSet = ResetEventSlim?.Wait(TimeSpan.FromMinutes(1));

        return isSet ?? false;
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