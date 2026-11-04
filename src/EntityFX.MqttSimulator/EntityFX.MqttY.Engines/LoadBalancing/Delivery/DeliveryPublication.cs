namespace EntityFX.MqttY.LoadBalancing;

internal readonly record struct DeliveryPublication(int Publisher, int Backend, bool Measurement, long Tick);
