using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Xunit;

namespace EntityFX.Tests.Web;

public class VersionedPersistenceTests
{
    [Fact]
    public async Task Read_MigratesV1DocumentToCurrentVersion()
    {
        await WithStore(async store =>
        {
            var path = store.Resolve("topologies/v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                "{ \"id\": \"v1\", \"name\": \"legacy\", \"graph\": { \"nodes\": [], \"networks\": [], \"links\": [] }, \"spec\": {} }");

            var saved = await store.ReadAsync<SavedTopologyDto>("topologies/v1.json");

            Assert.NotNull(saved);
            Assert.Equal(PersistenceMigrations.CurrentVersion, saved!.SchemaVersion);
            Assert.Equal("legacy", saved.Name);
        });
    }

    [Fact]
    public async Task CorruptDocument_DoesNotEscapeTryRead()
    {
        await WithStore(async store =>
        {
            var path = store.Resolve("jobs/broken.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{ not json");

            Assert.Null(await store.TryReadAsync<SimulationJob>("jobs/broken.json"));
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(
                () => store.ReadAsync<SimulationJob>("jobs/broken.json"));
        });
    }

    [Fact]
    public async Task ConcurrentWrites_AreAtomicAndLeaveNoTemporaryFiles()
    {
        await WithStore(async store =>
        {
            var writes = Enumerable.Range(0, 20).Select(value =>
                store.WriteAsync("jobs/job.json", new SimulationJob { Name = $"job-{value}" }));
            await Task.WhenAll(writes);

            var job = await store.ReadAsync<SimulationJob>("jobs/job.json");
            Assert.NotNull(job);
            Assert.StartsWith("job-", job!.Name);
            Assert.Equal(0, Directory.GetFiles(Path.GetDirectoryName(store.Resolve("jobs/job.json"))!, "*.tmp").Length);
        });
    }

    [Fact]
    public async Task Prune_KeepsNewestDocuments()
    {
        await WithStore(store =>
        {
            var directory = Path.GetDirectoryName(store.Resolve("monitoring/x.json"))!;
            Directory.CreateDirectory(directory);
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(directory, $"{i}.json");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
            }

            Assert.Equal(3, store.Prune("monitoring", "*.json", 2));
            Assert.Equal(new[] { "3.json", "4.json" },
                Directory.GetFiles(directory, "*.json").Select(Path.GetFileName).OrderBy(name => name).ToArray());
            return Task.CompletedTask;
        });
    }

    private static async Task WithStore(Func<JsonFileStore, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-persistence-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonFileStore(root);
            await test(store);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
