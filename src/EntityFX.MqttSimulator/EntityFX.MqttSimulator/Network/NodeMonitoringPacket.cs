using EntityFX.MqttY.Contracts.Network;
using System.Diagnostics;

internal class NodeMonitoringPacket
{
    public NetworkPacket? RequestPacket { get; set; }

    public long RequestTick { get; init; }

    public NetworkPacket? ResponsePacket { get; set; }

    public long? ResponseTick { get; set; }

    public Guid Id { get; set; }

    public ManualResetEventSlim? ResetEventSlim { get; set; }

    public bool IsSet { get; set; }

    public bool IsExpired { get; set; } = false;

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

    public bool WaitIsSet(TimeSpan timeout)
    {
        return ResetEventSlim?.Wait(TimeSpan.FromMinutes(1)) ?? false;

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
