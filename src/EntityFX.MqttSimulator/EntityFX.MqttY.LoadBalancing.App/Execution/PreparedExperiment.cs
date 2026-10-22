namespace EntityFX.MqttY.LoadBalancing;

public sealed record PreparedExperiment(LoadBalancingOptions Options, int[] Assignments,
    InputSnapshot Configuration, ProfileSnapshot? Profile, InputSnapshot? CalibrationObservations);
