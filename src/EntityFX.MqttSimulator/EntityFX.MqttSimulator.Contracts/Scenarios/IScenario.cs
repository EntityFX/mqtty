using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace EntityFX.MqttY.Contracts.Scenarios
{

    public interface IScenario: IExecutable
    {

        public IScenario? Next { get; set; }
    }


    public interface IScenario<TContext> : IScenario
    {
        public IImmutableDictionary<int, IAction<TContext>> Actions { get; init; }

        public TContext Context { get; init; }
    }
}
