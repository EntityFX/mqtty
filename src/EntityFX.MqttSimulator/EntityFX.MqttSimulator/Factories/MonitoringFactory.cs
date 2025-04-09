using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class MonitoringFactory : IFactory<INetworkLogger, NetworkGraphFactoryOption>
{
    public INetworkLogger Configure(NetworkGraphFactoryOption options, INetworkLogger service)
    {
        return service;
    }

    public INetworkLogger Create(NetworkGraphFactoryOption options)
    {
        var monitoring = new NetworkLogger(
            options.MonitoringOption.ScopesEnabled, options.TicksOption.TickPeriod, options.MonitoringOption.Ignore);

        IINetworkLoggerProvider? monitoringProvider = null;

        if (string.IsNullOrEmpty(options.MonitoringOption.Type) || options.MonitoringOption.Type == "null")
        {
            monitoringProvider = new NullNetworkLoggerProvider(monitoring);
        }

        if (options.MonitoringOption.Type == "console")
        {
            monitoringProvider = new ConsoleNetworkLoggerProvider(monitoring);
        }

        if (options.MonitoringOption.Type == "text-file")
        {
            monitoringProvider = new TextFileNetworkLoggerProvider(
                monitoring, options.MonitoringOption.Path ?? string.Empty);
        }

        monitoringProvider?.Start();

        return monitoring;
    }
}