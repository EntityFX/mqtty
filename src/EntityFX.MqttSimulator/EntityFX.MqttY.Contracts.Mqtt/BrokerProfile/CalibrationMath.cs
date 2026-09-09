namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public static class CalibrationMath
    {
        public static double ConditionalDeliveryLoss(
            double rateFailure, double targetFailure, double tolerance = 1e-12)
        {
            ValidateProbability(rateFailure, nameof(rateFailure));
            ValidateProbability(targetFailure, nameof(targetFailure));
            if (rateFailure > targetFailure + tolerance)
                throw new InvalidOperationException(
                    "Rate rejection exceeds target failure; raise calibrated capacity.");
            if (rateFailure >= 1.0) return 0.0;
            return Math.Clamp((targetFailure - rateFailure) / (1.0 - rateFailure), 0.0, 1.0);
        }

        private static void ValidateProbability(double value, string name)
        {
            if (!double.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentOutOfRangeException(name, "Probability must be finite and in [0, 1].");
        }
    }
}
