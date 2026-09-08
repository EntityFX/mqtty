using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IMqttBroker : IServer
    {
        BrokerMetricsSnapshot GetMetrics();
        //void Start();

        //void Stop();
    }

}
