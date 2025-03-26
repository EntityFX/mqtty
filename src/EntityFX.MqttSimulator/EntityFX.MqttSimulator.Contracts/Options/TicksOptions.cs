namespace EntityFX.MqttY.Contracts.Options
{
    public class TicksOptions
    {
        public TimeSpan TickPeriod { get; set; } = TimeSpan.FromMilliseconds(0.1);
        public TimeSpan ReceiveWaitPeriod { get; set; } = TimeSpan.FromSeconds(30);

        public int NetworkTicks { get; set; } = 5;
    }
}
