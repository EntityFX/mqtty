using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.Concurrent;

public class Monitoring : IMonitoring
{
    public event EventHandler<MonitoringItem>? Added;

    private ConcurrentDictionary<DateTimeOffset, MonitoringItem> _storage = new ConcurrentDictionary<DateTimeOffset, MonitoringItem>();

    public IEnumerable<MonitoringItem> Items => _storage.Values.Take(10000);

    public void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet, MonitoringType type, string category, Guid scope, object details)
    {
        var item = new MonitoringItem(
            Guid.NewGuid(), DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, string.Empty, details, category);

        _storage.TryAdd(item.Date, item);

        Added?.Invoke(this, item);
    }


    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, string category, Guid scope, object details)
    {
        Push(from.Address,
            from.NodeType, to.Address, to.NodeType,
            packet, type, category, scope, details);
    }
}
