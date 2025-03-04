namespace EntityFX.MqttY.Contracts.Scenarios
{

    public interface IAction<TContext> : IExecutable
    {
        int Index { get; init; }

        TimeSpan? Timeout { get; init; }

        TimeSpan? Delay { get; init; }

        int Iterrations { get; init; }

        ActionType Type { get; init; }

        public TContext? Context { get; set; }
    }

    public interface IAction<TContext, TConfig> : IAction<TContext>
    {
        public TConfig? Config { get; set; }
    }
}
