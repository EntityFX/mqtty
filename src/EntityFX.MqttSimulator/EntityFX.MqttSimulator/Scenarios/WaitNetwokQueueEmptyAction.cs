namespace EntityFX.MqttY.Scenarios
{
    public class WaitNetwokQueueEmptyAction : ScenarioAction<NetworkSimulation, WaitNetwokQueueEmptyOptions>
    {
        public WaitNetwokQueueEmptyAction()
        {
            
        }

        public override async Task ExecuteAsync()
        {
            if (Config == null)
            {
                throw new ArgumentNullException(nameof(Config));
            }

            if (Config.WaitTimeOut == TimeSpan.Zero)
            {
                return;
            }

            while (true)
            {
                await Task.Delay(Config.WaitTimeOut);

                var networks = Context!.NetworkGraph?.Networks;

                if (networks?.Any() != true)
                {
                    return;
                }

                var allQueuesAreEmpty = networks.Values.All(n => n.QueueSize == 0);

                if (allQueuesAreEmpty)
                {
                    return;
                }
            }
        }
    }
}
