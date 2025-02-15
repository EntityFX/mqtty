namespace EntityFX.MqttY.Contracts.Utils;

public interface IFactory<out TService, in TOptions>
{
    TService Create(TOptions options);
}