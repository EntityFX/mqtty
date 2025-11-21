using EntityFX.MqttY.Contracts.Network;

internal class NodeMonitoringPacket
{
    public NetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public NetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public Guid Id { get; set; }

    public ManualResetEventSlim? ReceiveResetEventSlim { get; set; }

    public bool ReceiveIsSet { get; set; }

    public bool IsExpired { get; set; } = false;

    internal long WaitTicks => _waitTicks;

    internal long DelayTick { get => _delayTicks; init => _delayTicks = value; }

    private long _waitTicks = 600000;
    private long _delayTicks = 1;

    public string Marker { get; set; } = string.Empty;

    internal void ReduceDelayTicks()
    {
        Interlocked.Decrement(ref _delayTicks);
    }

    internal void ReduceWaitTicks()
    {
        Interlocked.Decrement(ref _waitTicks);

        if (_waitTicks <= 0)
        {
            ReceiveResetEventSlim?.Set();
        }
    }

    public bool WaitIsSet(TimeSpan timeout)
    {
        return ReceiveResetEventSlim?.Wait(TimeSpan.FromMinutes(1)) ?? false;

        //var sw = new Stopwatch();
        //sw.Start();
        //while (true)
        //{
        //    if (IsSet)
        //    {
        //        return true;
        //    }

        //    if (sw.Elapsed >= timeout)
        //    {
        //        return false;
        //    }
        //}
    }


} 
