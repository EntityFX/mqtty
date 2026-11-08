using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;

namespace EntityFX.Tests.Web;

[TestClass]
public class VersionedPersistenceTests
{
    [TestMethod]
    public async Task Read_MigratesV1DocumentToCurrentVersion()
    {
        WithStore(async store =>
        {
            var path = store.Resolve("topologies/v1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,
                "{ \"id\": \"v1\", \"name\": \"legacy\", \"graph\": { \"nodes\": [], \"networks\": [], \"links\": [] }, \"spec\": {} }");

            var saved = await store.ReadAsync<SavedTopologyDto>("topologies/v1.json");

            Assert.IsNotNull(saved);
            Assert.AreEqual(PersistenceMigrations.CurrentVersion, saved!.SchemaVersion);
            Assert.AreEqual("legacy", saved.Name);
        });
    }

    [TestMethod]
    public async Task CorruptDocument_DoesNotEscapeTryRead()
    {
        WithStore(async store =>
        {
            var path = store.Resolve("jobs/broken.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "{ not json");

            Assert.IsNull(await store.TryReadAsync<SimulationJob>("jobs/broken.json"));
            await Assert.ThrowsExceptionAsync<System.Text.Json.JsonException>(
                () => store.ReadAsync<SimulationJob>("jobs/broken.json"));
        });
    }

    [TestMethod]
    public async Task ConcurrentWrites_AreAtomicAndLeaveNoTemporaryFiles()
    {
        WithStore(async store =>
        {
            var writes = Enumerable.Range(0, 20).Select(value =>
                store.WriteAsync("jobs/job.json", new SimulationJob { Name = $"job-{value}" }));
            await Task.WhenAll(writes);

            var job = await store.ReadAsync<SimulationJob>("jobs/job.json");
            Assert.IsNotNull(job);
            StringAssert.StartsWith(job!.Name, "job-");
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(store.Resolve("jobs/job.json"))!, "*.tmp").Length);
        });
    }

    [TestMethod]
    public async Task Prune_KeepsNewestDocuments()
    {
        WithStore(store =>
        {
            var directory = Path.GetDirectoryName(store.Resolve("monitoring/x.json"))!;
            Directory.CreateDirectory(directory);
            for (var i = 0; i < 5; i++)
            {
                var path = Path.Combine(directory, $"{i}.json");
                File.WriteAllText(path, "{}");
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(i));
            }

            Assert.AreEqual(3, store.Prune("monitoring", "*.json", 2));
            CollectionAssert.AreEquivalent(new[] { "3.json", "4.json" },
                Directory.GetFiles(directory, "*.json").Select(Path.GetFileName).ToArray());
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
