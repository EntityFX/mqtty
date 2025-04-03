namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IIncrementable : ICounter
    {
        void Increment();
    }
}
