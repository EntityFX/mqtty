namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Статус узла сети (для диалога статуса брокера).</summary>
public sealed class NodeStatusDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Specification { get; set; } = string.Empty;

    public string? Network { get; set; }

    public bool Connected { get; set; }

    public long QueueLength { get; set; }

    public IReadOnlyDictionary<string, long> Counters { get; set; } = new Dictionary<string, long>();
}
