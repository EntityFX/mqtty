namespace EntityFX.MqttY.Contracts.Scenarios
{
    public class ScenarioAction<TContext, TConfig> : IAction<TContext, TConfig>
    {
        public int Index { get; init; }

        public string Name { get; init; } = "ScenarioAction";

        public TimeSpan? ActionTimeout { get; init; }

        public TimeSpan? IterrationsTimeout { get; init; }

        public int Iterrations { get; init; } = 1;
        public TimeSpan? Delay { get; init; }
        public ActionType Type { get; init; }
        public TContext? Context { get; set; }
        public TConfig? Config { get; set; }
        public ScenarioAction()
        {
            Iterrations = 1;
        }

        public virtual Task ExecuteAsync()
        {
            return Task.CompletedTask;
        }
    }
}
