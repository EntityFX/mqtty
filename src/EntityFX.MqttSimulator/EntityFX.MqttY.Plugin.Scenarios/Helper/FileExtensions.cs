namespace EntityFX.MqttY.Plugin.Scenarios.Helper;

public static class FileExtensions
{
    public static void CreateDirectory(string nodePath)
    {
        if (!Directory.Exists(nodePath))
        {
            Directory.CreateDirectory(nodePath);
        }
    }

    public static string ReplaceParams(string source, string scenario)
    {
        return source
            .Replace("{scenario}", scenario)
            .Replace("{date}", $"{DateTime.Now:yyyy_MM_dd__HH_mm}");
    }
}