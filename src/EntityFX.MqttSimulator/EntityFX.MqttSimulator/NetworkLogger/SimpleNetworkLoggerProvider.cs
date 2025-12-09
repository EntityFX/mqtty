using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Text;

internal class SimpleNetworkLoggerProvider : NetworkLoggerBase, INetworkLoggerProvider
{
    private readonly StringBuilder stringBuilder;

    public SimpleNetworkLoggerProvider(INetworkLogger monitoring, StringBuilder stringBuilder)
        : base(monitoring)
    {
        this.stringBuilder = stringBuilder;
    }

    protected override void WriteScope(NetworkLoggerScope scope)
    {

    }

    protected override void WriteItem(NetworkLoggerItem item)
    {
        stringBuilder.AppendLine(GetMonitoringLine(item));
    }

    protected override string GetMonitoringLine(NetworkLoggerItem item)
    {
        return string.Format("{0}: {1} {3} {2}", item.Tick, item.From, item.To, item.Type);
    }
}
