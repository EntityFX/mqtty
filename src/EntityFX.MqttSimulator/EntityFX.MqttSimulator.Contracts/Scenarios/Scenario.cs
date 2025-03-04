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

                var iterrations = action.Value.Iterrations;

                for (int i = 0; i < iterrations; i++)
                {
                    var actionTask = action.Value.ExecuteAsync();

                    if (action.Value.Timeout > TimeSpan.Zero)
                    {
                        await actionTask.WaitAsync(action.Value.Timeout.Value);
                    }

                    actionTask.Wait();
                }
            }
        }
    }
}
