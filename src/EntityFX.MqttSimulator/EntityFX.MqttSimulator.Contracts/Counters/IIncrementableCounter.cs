namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IIncrementableCounter : ICounter
    {
        void Increment();
    }
}
