using EntityFX.MqttY.Web.App.Services;
using Xunit;
namespace EntityFX.Tests.Web;

public sealed class FileStoreSafetyTests
{
    [Fact]
    public void StorageGuardRejectsImpossibleReserveWithoutStartingOrDeletingAnything()
    {
        var data = Path.GetFullPath(Path.GetTempPath());
        SimulationStorageGuard.Check(new DataDirectoryOptions { MinimumFreeDiskBytes = 0 }, data);
        Assert.Throws<InvalidOperationException>(() => SimulationStorageGuard.Check(new DataDirectoryOptions { MinimumFreeDiskBytes = long.MaxValue }, data));
        Assert.Throws<InvalidOperationException>(() => SimulationStorageGuard.Check(new DataDirectoryOptions { RequiredMountPoint = Path.Combine(data, "not-a-mount") }, data));
    }
    [Fact]
    public void SiblingPrefixAndAbsolutePathsDoNotEscapeRoot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mqtty-path-" + Guid.NewGuid().ToString("N"));
        var store = new JsonFileStore(directory);
        try
        {
            Assert.Throws<InvalidOperationException>(() => store.Resolve("../" + Path.GetFileName(directory) + "-evil/data.json"));
            Assert.Throws<InvalidOperationException>(() => store.Resolve(Path.Combine(directory, "absolute.json")));
            Assert.StartsWith(directory + Path.DirectorySeparatorChar, store.Resolve("valid/data.json"));
        }
        finally { Directory.Delete(directory); }
    }
}
