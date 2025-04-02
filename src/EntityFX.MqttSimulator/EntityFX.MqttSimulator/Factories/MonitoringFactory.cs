using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class MonitoringFactory : IFactory<IMonitoring, MonitoringOption>
{
    public IMonitoring Configure(MonitoringOption options, IMonitoring service)
    {
        return service;
    }

    public IMonitoring Create(MonitoringOption options)
    {
        var monitoring = new Monitoring(options.ScopesEnabled, options.Ignore);

        IMonitoringProvider? monitoringProvider = null;
        if (options.Type == "console")
        {
            monitoringProvider = new ConsoleMonitoringProvider(monitoring);
        }

        if (options.Type == "text-file")
        {
            monitoringProvider = new TextFileMonitoringProvider(monitoring, options.Path);
        }

        monitoringProvider?.Start();

        return monitoring;
    }
}