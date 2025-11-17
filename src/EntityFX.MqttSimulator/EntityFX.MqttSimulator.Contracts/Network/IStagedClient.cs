namespace EntityFX.MqttY.Contracts.Network
{
    public interface IStagedClient
    {
        bool BeginConnect(string server);

        bool CompleteConnect(ResponsePacket response);

        bool BeginDisconnect();

        bool CompleteDisconnect();
    }
}
