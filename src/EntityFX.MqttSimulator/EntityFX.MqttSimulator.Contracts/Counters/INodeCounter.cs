namespace EntityFX.MqttY.Contracts.Counters
{
    public interface INodeCounter<TValue> : ICounter<TValue>, 
        IIncrementableCounter, IAddableCounter
        where TValue : struct, IEquatable<TValue>
    {

    }
}
