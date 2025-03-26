using EntityFX.MqttY.Contracts.Network;

internal class NodePacket
{
    public Packet? Packet { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public ManualResetEventSlim? ResetEventSlim { get; set; }

    internal long WaitTime => _waitTime;

    private long _waitTime = 600000;

    internal void ReduceWaitTime()
    {
        Interlocked.Decrement(ref _waitTime);

        if (_waitTime <= 0)
        {
            ResetEventSlim?.Set();
        }
    }


} 
