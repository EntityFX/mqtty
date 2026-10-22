namespace EntityFX.MqttY.LoadBalancing;

public sealed record ProfileInput(string BrokerType, string BrokerProfilePath, string CalibrationObservationsPath);
