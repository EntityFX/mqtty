namespace EntityFX.MqttY.LoadBalancing;

/// <summary>Assigns connections once; publishing never changes these assignments.</summary>
public static class ConnectionPolicies
{
    public static int[] Assign(BalancingMode mode, int publishers, int backendCount)
    {
        if (!Enum.IsDefined(typeof(BalancingMode), mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (publishers <= 0)
            throw new ArgumentOutOfRangeException(nameof(publishers), "At least one publisher is required.");
        if (backendCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(backendCount), "At least one backend is required.");
        if (backendCount > publishers || mode == BalancingMode.Single && backendCount != 1)
            throw new ArgumentException("Each backend needs publishers; single requires one backend.");

        var assignments = new int[publishers];
        if (mode == BalancingMode.Single)
            return assignments;

        if (mode is BalancingMode.DirectBalanced or BalancingMode.RoundRobin)
        {
            // The direct control deliberately has the same map as round robin.
            // With no modeled proxy overhead it must not invent a performance gain.
            for (var publisher = 0; publisher < publishers; publisher++)
                assignments[publisher] = publisher % backendCount;
            return assignments;
        }

        var reservedConnections = new int[backendCount];
        for (var publisher = 0; publisher < publishers; publisher++)
        {
            var selected = 0;
            for (var backend = 1; backend < backendCount; backend++)
                if (reservedConnections[backend] < reservedConnections[selected])
                    selected = backend;

            assignments[publisher] = selected;
            // Reserve before CONNECT completes, including simultaneous pending connects.
            reservedConnections[selected]++;
        }
        return assignments;
    }
}
