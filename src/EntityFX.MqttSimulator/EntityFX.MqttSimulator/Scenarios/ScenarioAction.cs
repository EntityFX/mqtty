using EntityFX.MqttY.Contracts.Scenarios;

namespace EntityFX.MqttY.Scenarios
{
    public class ScenarioAction<TContext, TConfig> : IAction<TContext, TConfig>
    {
        private bool _disposedValue;

        public int Index { get; init; }

        public string Name { get; init; } = "ScenarioAction";

        public TimeSpan? ActionTimeout { get; init; }

        public TimeSpan? IterationsTimeout { get; init; }

        public int Iterations { get; init; } = 1;
        public TimeSpan? Delay { get; init; }
        public ActionType Type { get; init; }
        public TContext? Context { get; set; }
        public TConfig? Config { get; set; }
        
        public IServiceProvider? ServiceProvider { get; init;  }

        public IScenario<TContext>? Scenario { get; init; }

        object? IExecutable.Context => Context;

        public ScenarioAction()
        {
            Iterations = 1;
        }

        public ScenarioAction(IServiceProvider serviceProvider, IScenario<TContext> scenario)
        {
            Iterations = 1;
            Scenario = scenario;
            ServiceProvider = serviceProvider;
        }

        public virtual Task ExecuteAsync()
        {
            return Task.CompletedTask;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Finish();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                _disposedValue = true;
            }
        }

        protected virtual void Finish()
        {
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
