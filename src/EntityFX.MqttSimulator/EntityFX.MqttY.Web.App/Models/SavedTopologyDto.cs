namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Сохранённая пользовательская топология (граф + параметры генерации).</summary>
public sealed class SavedTopologyDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public GraphDto Graph { get; set; } = new();

    public TopologySpecDto Spec { get; set; } = new();
}
