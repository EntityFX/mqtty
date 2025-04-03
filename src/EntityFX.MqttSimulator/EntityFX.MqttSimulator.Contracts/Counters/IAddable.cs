namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IAddable : ICounter
    {
        void Add(long value);
    }
}
