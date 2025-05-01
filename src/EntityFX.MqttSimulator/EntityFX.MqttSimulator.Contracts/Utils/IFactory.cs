namespace EntityFX.MqttY.Contracts.Utils;

public interface IFactory<TService, TApplicationOptions>
{
    TService Create(TApplicationOptions options);

    TService Configure(TApplicationOptions options, TService service);
}