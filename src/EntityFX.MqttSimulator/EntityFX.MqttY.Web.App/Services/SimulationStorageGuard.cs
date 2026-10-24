namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Не допускает старт при исчезнувшем evidence mount и недостаточном запасе диска.</summary>
public static class SimulationStorageGuard
{
    public static void Check(DataDirectoryOptions options, string dataRoot)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.IsNullOrWhiteSpace(options.RequiredMountPoint))
        {
            var mount = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RequiredMountPoint));
            if (!dataRoot.StartsWith(Prefix(mount), comparison) && !string.Equals(dataRoot, mount, comparison))
                throw new InvalidOperationException("Data directory is outside the required evidence mount.");
            if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("RequiredMountPoint is supported on Linux only.");
            var mounted = File.ReadLines("/proc/self/mountinfo").Any(line =>
            {
                var fields = line.Split(' ');
                if (fields.Length < 5) return false;
                var name = fields[4].Replace("\\040", " ").Replace("\\011", "\t").Replace("\\134", "\\");
                return string.Equals(name, mount, StringComparison.Ordinal);
            });
            if (!mounted) throw new InvalidOperationException("Required evidence disk is not mounted; scientific execution is disabled.");
        }
        var drive = DriveInfo.GetDrives().Where(d => dataRoot.StartsWith(Prefix(d.Name), comparison) || string.Equals(dataRoot, d.Name, comparison))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault();
        if (drive == null || !drive.IsReady || drive.AvailableFreeSpace < Math.Max(0, options.MinimumFreeDiskBytes))
            throw new InvalidOperationException("Insufficient free disk space for a simulation; previous evidence is retained.");
    }
    private static string Prefix(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
