using System.Collections.Concurrent;

namespace EntityFX.MqttY.Mqtt.Internals
{
    internal class InMemoryRepository<T> : IRepository<T>
        where T : IStorageObject
    {
        readonly ConcurrentDictionary<string, T> elements = new ConcurrentDictionary<string, T>();

        public IEnumerable<T> ReadAll() => elements.Select(e => e.Value);

        public T? Read(string id)
        {
            elements.TryGetValue(id, out T? element);

            return element;
        }

        public void Create(T element) => elements.TryAdd(element.Id, element);

        public void Update(T element) => elements.AddOrUpdate(element.Id, element, (key, value) => element);

        public void Delete(string id) => elements.TryRemove(id, out _);
    }
}
