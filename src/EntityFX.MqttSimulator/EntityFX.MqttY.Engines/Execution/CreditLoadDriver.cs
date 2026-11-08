using System.Collections.Generic;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Experiment;

/// <summary>
/// Shared open-loop load driver used by the scientific runners. The scheduler is
/// cumulative (it keeps fractional offered load from being rounded away) and
/// hands each publisher its own credit line, so the same code can run serially
/// or through <see cref="Parallel.For"/> without changing the offered rate.
/// </summary>
public static class CreditLoadDriver
{
    /// <summary>
    /// Drives <paramref name="ticks"/> model ticks. Returns the number of
    /// publication slots that became due over the whole window.
    /// </summary>
    public static long Drive(
        int publishers,
        long ticks,
        double perTick,
        Func<int, bool> hasPendingPublish,
        Action<int> publish,
        Action onTick,
        bool publishInParallel)
    {
        if (publishers <= 0)
        {
            return 0;
        }

        var credit = new long[publishers];
        var creditCap = checked((long)Math.Ceiling(perTick / publishers) + 1);
        long planned = 0;
        long totalSlots = 0;
        var nextScheduledPublisher = 0;

        for (long tick = 0; tick < ticks; tick++)
        {
            // Compute the cumulative schedule to avoid repeatedly rounding away
            // fractional intensity.
            var due = checked((long)Math.Floor((tick + 1) * perTick + 1e-9));
            var slots = due - planned;
            planned = due;
            totalSlots += slots;

            var each = slots / publishers;
            var extra = (int)(slots % publishers);

            if (publishInParallel)
            {
                var maxDop = int.TryParse(Environment.GetEnvironmentVariable("MQTTY_WORKERS"), out var workers) && workers > 0
                    ? workers
                    : Environment.ProcessorCount;
                Parallel.For(0, publishers, new ParallelOptions { MaxDegreeOfParallelism = maxDop }, publisher =>
                {
                    var offset = (publisher - nextScheduledPublisher + publishers) % publishers;
                    credit[publisher] = Math.Min(creditCap, credit[publisher] + each + (offset < extra ? 1 : 0));
                    while (credit[publisher] > 0 && !hasPendingPublish(publisher))
                    {
                        publish(publisher);
                        credit[publisher]--;
                    }
                });
            }
            else
            {
                for (var publisher = 0; publisher < publishers; publisher++)
                {
                    var offset = (publisher - nextScheduledPublisher + publishers) % publishers;
                    credit[publisher] = Math.Min(creditCap, credit[publisher] + each + (offset < extra ? 1 : 0));
                    while (credit[publisher] > 0 && !hasPendingPublish(publisher))
                    {
                        publish(publisher);
                        credit[publisher]--;
                    }
                }
            }

            nextScheduledPublisher = (nextScheduledPublisher + extra) % publishers;
            onTick();
        }

        return totalSlots;
    }
}
