namespace EntityFX.MqttY.Web.App.Models;

public sealed class TwinValidationCreateDto
{
    public string Name { get; set; } = "Одиночная проверка статьи 1";
    public string BrokerPresetId { get; set; } = "mosquittodefault";
    public int Publishers { get; set; } = 16;
    public int Qos { get; set; } = 1;
    public int Seed { get; set; } = 42;
    public double WarmupSeconds { get; set; } = 1;
    public double MeasurementSeconds { get; set; } = 2;
    public double DrainSeconds { get; set; } = 5;
    public double TickSeconds { get; set; } = .00001;
    public bool ValidateOnly { get; set; }
    public int TimeLimitSeconds { get; set; } = 600;
}
