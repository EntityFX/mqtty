namespace EntityFX.MqttY.Contracts.Options
{
    public class TicksOptions
    {
        public TimeSpan TickPeriod { get; set; }
        public TimeSpan ReceiveWaitPeriod { get; set; }

        public int NetworkTicks { get; set; }
        
        public int CounterHistoryDepth { get; set; }
    }
}
