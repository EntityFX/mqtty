using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Scenarios
{

    public interface IScenario: IExecutable
    {

        public IScenario? Next { get; set; }
    }


    public interface IScenario<TContext> : IScenario
    {
        public IImmutableDictionary<int, IAction<TContext>> Actions { get; init; }

        public new TContext Context { get; init; }
    }
}
