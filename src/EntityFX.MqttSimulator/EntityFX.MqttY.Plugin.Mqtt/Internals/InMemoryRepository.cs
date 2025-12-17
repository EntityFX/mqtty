using System.Collections.Concurrent;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class InMemoryRepository<T> : IRepository<T>
        where T : IStorageObject
    {
        readonly ConcurrentDictionary<string, T> _elements = new ConcurrentDictionary<string, T>();

        public IEnumerable<T> ReadAll() => _elements.Select(e => e.Value);

        public T? Read(string id)
        {
            _elements.TryGetValue(id, out T? element);

            return element;
        }

        public void Create(T element) => _elements.TryAdd(element.Id, element);

        public void Update(T element) => _elements.AddOrUpdate(element.Id, element, (key, value) => element);

        public void Delete(string id) => _elements.TryRemove(id, out _);

        public void Clear()
        {
            _elements.Clear();
        }
    }
}
