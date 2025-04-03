using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class MonitoringFactory : IFactory<IMonitoring, NetworkGraphFactoryOption>
{
    public IMonitoring Configure(NetworkGraphFactoryOption options,IMonitoring service)
    {
        return service;
    }

    public IMonitoring Create(NetworkGraphFactoryOption options)
    {
        var monitoring = new Monitoring(options.MonitoringOption.ScopesEnabled, options.TicksOption.TickPeriod, options.MonitoringOption.Ignore);

        IMonitoringProvider? monitoringProvider = null;
        if (options.MonitoringOption.Type == "console")
        {
            monitoringProvider = new ConsoleMonitoringProvider(monitoring);
        }

        if (options.MonitoringOption.Type == "text-file")
        {
            monitoringProvider = new TextFileMonitoringProvider(monitoring, options.MonitoringOption.Path ?? string.Empty);
        }

        monitoringProvider?.Start();

        return monitoring;
    }
}