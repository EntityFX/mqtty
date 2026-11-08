using System.Collections.Concurrent;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class InMemoryRepository<T> : IRepository<T>
        where T : IStorageObject
    {
        readonly ConcurrentDictionary<string, T> _elements = new ConcurrentDictionary<string, T>();

        // ConcurrentDictionary.Values snapshots the whole table under every
        // bucket lock. ReadAll() is called per publisher per tick (HasPendingPublish)
        // and per node during drain (IsQuiescent), so keep a lock-free snapshot.
        // It is rebuilt only on membership changes or when Update replaces the
        // stored instance; the MQTT code updates the same ClientSession object,
        // which used to trigger an O(sessions) copy on every protocol message.
        private readonly object _snapshotGate = new();
        private volatile T[] _snapshot = Array.Empty<T>();

        public IEnumerable<T> ReadAll() => _snapshot;

        public T? Read(string id)
        {
            _elements.TryGetValue(id, out T? element);

            return element;
        }

        public void Create(T element)
        {
            if (_elements.TryAdd(element.Id, element))
            {
                RebuildSnapshot();
            }
        }

        public void Update(T element)
        {
            if (_elements.TryGetValue(element.Id, out var existing) && ReferenceEquals(existing, element))
            {
                return;
            }

            _elements[element.Id] = element;
            RebuildSnapshot();
        }

        public void Delete(string id)
        {
            if (_elements.TryRemove(id, out _))
            {
                RebuildSnapshot();
            }
        }

        public void Clear()
        {
            lock (_snapshotGate)
            {
                _elements.Clear();
                _snapshot = Array.Empty<T>();
            }
        }

        private void RebuildSnapshot()
        {
            lock (_snapshotGate)
            {
                _snapshot = _elements.Values.ToArray();
            }
        }
    }
}
