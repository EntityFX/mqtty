using EntityFX.MqttY.Contracts.Network;

internal class NodeMonitoringPacket
{
    public NetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public NetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public Guid Id { get; set; }

    public ManualResetEventSlim? ResetEventSlim { get; set; }

    internal long WaitTicks => _waitTicks;

    private long _waitTicks = 600000;

    public string Marker { get; set; } = string.Empty;

    internal void ReduceWaitTicks()
    {
        Interlocked.Decrement(ref _waitTicks);

        if (_waitTicks <= 0)
        {
            ResetEventSlim?.Set();
        }
    }


} 
