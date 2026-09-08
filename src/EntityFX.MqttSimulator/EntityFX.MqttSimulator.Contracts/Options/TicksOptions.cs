namespace EntityFX.MqttY.Contracts.Options
{
    public class TicksOptions
    {
        public TimeSpan TickPeriod { get; set; }
        public TimeSpan ReceiveWaitPeriod { get; set; }

        public int OutgoingWaitTicks { get; set; }
        
        public int CounterHistoryDepth { get; set; }

        public void Validate()
        {
            if (TickPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(TickPeriod));
            if (OutgoingWaitTicks <= 0) throw new ArgumentOutOfRangeException(nameof(OutgoingWaitTicks));
        }
    }
}
