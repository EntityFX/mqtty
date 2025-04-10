using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class NetworkLoggerFactory : IFactory<INetworkLogger, NetworkGraphFactoryOption>
{
    public INetworkLogger Configure(NetworkGraphFactoryOption options, INetworkLogger service)
    {
        return service;
    }

    public INetworkLogger Create(NetworkGraphFactoryOption options)
    {
        INetworkLoggerProvider? monitoringProvider = null;
        INetworkLogger? logger = null;

        if (string.IsNullOrEmpty(options.MonitoringOption.Type) || options.MonitoringOption.Type == "null")
        {
            logger = new NullNetworkLogger();
        }

        if (options.MonitoringOption.Type == "console")
        {
            logger = new NetworkLogger(
            options.MonitoringOption.ScopesEnabled, options.TicksOption.TickPeriod, options.MonitoringOption.Ignore);
            monitoringProvider = new ConsoleNetworkLoggerProvider(logger);
        }

        if (options.MonitoringOption.Type == "text-file")
        {
            logger = new NetworkLogger(
            options.MonitoringOption.ScopesEnabled, options.TicksOption.TickPeriod, options.MonitoringOption.Ignore);
            monitoringProvider = new TextFileNetworkLoggerProvider(
                logger, options.MonitoringOption.Path ?? string.Empty);
        }

        monitoringProvider?.Start();

        return logger;
    }
}