using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
namespace EntityFX.MqttY.Federation;

public static class FederationRuntimeIdentity
{
    public static object Read()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, ".git")) && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
        string? Git(params string[] args)
        {
            if (directory == null) return null;
            try
            {
                var info = new ProcessStartInfo("git") { WorkingDirectory = directory.FullName, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (var a in args) info.ArgumentList.Add(a);
                using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEnd();
                return process.WaitForExit(5000) && process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                return null; // Portable deployments need not include a Git client or checkout.
            }
        }
        return new
        {
            framework = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            mqttYCommit = Git("rev-parse", "HEAD"),
            sourceStatus = Git("status", "--porcelain"),
            binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant())
        };
    }
}
