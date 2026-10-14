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
