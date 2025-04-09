namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IWriteableCounter<TValue>
        where TValue : struct, IEquatable<TValue>
    {
        void Set(TValue Value);
    }


}
