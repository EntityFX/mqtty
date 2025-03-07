using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Scenarios
{
    public class Scenario<TContext> : IScenario<TContext>
    {
        protected readonly IServiceProvider serviceProvider;

        public TContext Context { get; init; }

        public IImmutableDictionary<int, IAction<TContext>> Actions { get; init; }

        public IScenario? Next { get; set; }
        public string Name { get; init; } = nameof(Scenario<TContext>);

        public Scenario(IServiceProvider serviceProvider, TContext context, IImmutableDictionary<int, IAction<TContext>> actions)
        {
            this.serviceProvider = serviceProvider;
            Context = context;
            Actions = actions;
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

                if (action.Value.IterrationsTimeout != null)
                {
                    await ExecuteTimeoutIterationsAsync(action.Value, action.Value.IterrationsTimeout.Value);
                }
                else 
                {
                    await ExecuteIterationsAsync(action.Value, action.Value.Iterrations);
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
