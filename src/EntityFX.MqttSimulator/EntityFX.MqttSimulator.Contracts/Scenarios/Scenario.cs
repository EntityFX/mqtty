using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Scenarios
{
    public class Scenario<TContext> : IScenario<TContext>
    {
        protected readonly IServiceProvider serviceProvider;

        public TContext Context { get; init; }

        object? IExecutable.Context => Context;

        public IImmutableDictionary<int, IAction<TContext>> Actions { get; init; } 
            = new Dictionary<int, IAction<TContext>>().ToImmutableDictionary();

        public IScenario? Next { get; set; }
        public string Name { get; init; } = nameof(Scenario<TContext>);



        public Scenario(IServiceProvider serviceProvider, string scenario, TContext context,
            Func<IScenario<TContext>, Dictionary<int, IAction<TContext>>> actionsBuildFunc)
        {
            this.serviceProvider = serviceProvider;
            Name = scenario;
            Context = context;
            Actions = actionsBuildFunc(this).ToImmutableDictionary();
        }

        public async Task ExecuteAsync()
        {
            if (Actions?.Any() != true) return;

            foreach (var action in Actions)
            {
                action.Value.Context = Context;

                if (action.Value.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(action.Value.Delay.Value);
                }

                if (action.Value.IterationsTimeout != null)
                {
                    await ExecuteTimeoutIterationsAsync(action.Value, action.Value.IterationsTimeout.Value);
                }
                else 
                {
                    await ExecuteIterationsAsync(action.Value, action.Value.Iterations);
                }
            }
        }

        private async Task ExecuteIterationsAsync(IAction<TContext> action, int iterrations)
        {
            for (int i = 0; i < iterrations; i++)
            {
                var actionTask = action.ExecuteAsync();

                if (action.ActionTimeout > TimeSpan.Zero)
                {
                    await actionTask.WaitAsync(action.ActionTimeout.Value);
                }

                actionTask.Wait();
            }
        }

        private async Task ExecuteTimeoutIterationsAsync(IAction<TContext> action, TimeSpan timeout)
        {
            var start = DateTime.Now;
            while (DateTime.Now - start < timeout)
            {
                var actionTask = action.ExecuteAsync();

                if (action.ActionTimeout > TimeSpan.Zero)
                {
                    await actionTask.WaitAsync(action.ActionTimeout.Value);
                }

                actionTask.Wait();

            }
        }
    }
}
