using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Counters
{
    public interface ICounter
    {
        string Name { get; init; }

        string? UnitOfMeasure { get; init; }

        long Value { get; }

        void Refresh(long totalTicks);
    }
}
