namespace EntityFX.MqttY.Experiment;

/// <summary>Refresh-until-condition loop shared by the experiment runners.</summary>
public static class ExperimentLoops
{
    public static void Until(
        Func<bool> condition,
        long limitTicks,
        Action refresh,
        string timeoutMessage)
    {
        for (long tick = 0; !condition() && tick < limitTicks; tick++)
        {
            refresh();
        }

        if (!condition())
        {
            throw new TimeoutException(timeoutMessage);
        }
    }
}
