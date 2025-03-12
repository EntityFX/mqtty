namespace EntityFX.MqttY.Contracts.Scenarios
{
    public class ScenarioAction<TContext, TConfig> : IAction<TContext, TConfig>
    {
        public int Index { get; init; }

        public string Name { get; init; } = "ScenarioAction";

        public TimeSpan? ActionTimeout { get; init; }

        public TimeSpan? IterationsTimeout { get; init; }

        public int Iterations { get; init; } = 1;
        public TimeSpan? Delay { get; init; }
        public ActionType Type { get; init; }
        public TContext? Context { get; set; }
        public TConfig? Config { get; set; }

        object? IExecutable.Context => Context;

        public IScenario<TContext> Scenario { get; init; }

        public ScenarioAction(IScenario<TContext> scenario)
        {
            Iterations = 1;
            Scenario = scenario;
        }

        public virtual Task ExecuteAsync()
        {
            return Task.CompletedTask;
        }
    }
}
