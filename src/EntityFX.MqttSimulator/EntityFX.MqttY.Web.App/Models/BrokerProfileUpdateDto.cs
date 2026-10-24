namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Запрос на обновление содержимого профиля брокера.</summary>
public sealed class BrokerProfileUpdateDto
{
    public string ProfilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
