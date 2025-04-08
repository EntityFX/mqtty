using EntityFX.MqttY.Contracts.Counters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Counter
{
    internal class GenericCounter : INodeCounter, IWriteableCounter
    {
        public string Name { get; init; }

        public long Value => _value;

        public string? UnitOfMeasure { get; init; }

        private long _value = 0;

        public GenericCounter(string name)
        {
            Name = name;
        }

        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }

        public void Add(long value)
        {
            Interlocked.Add(ref _value, value);
        }

        public override string ToString()
        {
            return $"[{Name} = {Value}]";
        }

        public void Set(long value)
        {
            _value = value;
        }
    }
}
