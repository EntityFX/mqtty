namespace EntityFX.MqttY.Contracts.Counters
{
    public interface IWithCounters
    {
        CounterGroup Counters { get; set;  }
    }
}
