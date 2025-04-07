namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IAddableCounter : ICounter
    {
        void Add(long value);
    }
}
