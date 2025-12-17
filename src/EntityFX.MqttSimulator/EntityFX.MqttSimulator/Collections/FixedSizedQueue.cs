using System.Collections.Concurrent;

namespace EntityFX.MqttY.Collections
{
    public class FixedSizedQueue<T> : ConcurrentQueue<T>
    {
        private readonly object _syncObject = new object();

        public int Size { get; private set; }

        public FixedSizedQueue(int size)
        {
            Size = size;
        }

        public new void Enqueue(T obj)
        {
            base.Enqueue(obj);
            //lock (_syncObject)
            //{
                while (base.Count > Size)
                {
                    base.TryDequeue(out _);
                }
           // }
        }
    }
}
