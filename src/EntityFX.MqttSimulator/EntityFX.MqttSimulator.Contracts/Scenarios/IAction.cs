namespace EntityFX.MqttY.Contracts.Scenarios
{

    public interface IAction<TContext> : IExecutable
    {
        int Index { get; init; }

        TimeSpan? ActionTimeout { get; init; }

        TimeSpan? IterationsTimeout { get; init; }

        TimeSpan? Delay { get; init; }

        int Iterations { get; init; }

        ActionType Type { get; init; }

        public new TContext? Context { get; set; }

        IScenario<TContext> Scenario { get; init; }
    }

    public interface IAction<TContext, TConfig> : IAction<TContext>
    {
        public TConfig? Config { get; set; }
    }
}
