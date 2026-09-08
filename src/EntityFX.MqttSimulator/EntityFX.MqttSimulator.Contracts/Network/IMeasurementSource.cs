namespace EntityFX.MqttY.Contracts.Network
{
    public interface IMeasurementSource
    {
        void ResetMeasurement(long startTick);
    }
}
