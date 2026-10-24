using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EntityFX.Tests.Web;

/// <summary>WebApplicationFactory с временным каталогом данных для изоляции тестов.</summary>
public sealed class MqttYWebFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"mqtty-web-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("MqttYWeb:DataDirectory", DataDirectory);
        builder.UseSetting("MqttYWeb:MaxParallelJobs", "2");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            if (Directory.Exists(DataDirectory))
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
        }
        catch
        {
            // временный каталог может быть занят процессом
        }
    }
}
