namespace EntityFX.MqttY.Network;

/// <summary>
/// Best-effort physical-core count. The simulator refresh is synchronization
/// bound, so using logical (SMT) threads beyond the physical cores usually
/// makes the tick slower, not faster.
/// </summary>
internal static class CpuTopology
{
    public static int PhysicalCoreCount()
    {
        var logical = Environment.ProcessorCount;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                var coresPerSocket = lines
                    .Where(line => line.StartsWith("cpu cores", StringComparison.Ordinal))
                    .Select(line => int.Parse(line.Split(':')[1].Trim()))
                    .FirstOrDefault();
                var sockets = lines
                    .Where(line => line.StartsWith("physical id", StringComparison.Ordinal))
                    .Select(line => int.Parse(line.Split(':')[1].Trim()))
                    .Distinct()
                    .Count();

                if (coresPerSocket > 0 && sockets > 0)
                {
                    return Math.Clamp(coresPerSocket * sockets, 1, logical);
                }
            }
        }
        catch
        {
            // Fall through to the logical count.
        }

        return Math.Max(1, logical);
    }
}
