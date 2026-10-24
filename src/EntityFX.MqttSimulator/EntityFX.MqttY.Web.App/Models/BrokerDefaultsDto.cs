namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Параметры по умолчанию пресета брокера.</summary>
public sealed class BrokerDefaultsDto
{
    public int PayloadBytes { get; set; } = 16;

    public int Qos { get; set; } = 1;

    public int Publishers { get; set; } = 64;

    public int OfferedRps { get; set; } = 1000;
}
