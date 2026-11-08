using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Mechanism)]
public class InMemoryRepositoryTests
{
    private sealed class Item : IStorageObject
    {
        public string Id { get; init; } = string.Empty;
        public int Value { get; set; }
    }

    [TestMethod]
    public void Update_SameInstance_DoesNotRebuildSnapshot()
    {
        var repository = new InMemoryRepository<Item>();
        var item = new Item { Id = "a", Value = 1 };
        repository.Create(item);

        var before = repository.ReadAll();
        item.Value = 2;
        repository.Update(item);
        var after = repository.ReadAll();

        Assert.AreSame(before, after, "Updating the stored instance must not copy the snapshot.");
        Assert.AreEqual(2, repository.Read("a")!.Value);
        Assert.AreEqual(2, after.Single().Value);
    }

    [TestMethod]
    public void Update_Replacement_IsVisibleToReadAll()
    {
        var repository = new InMemoryRepository<Item>();
        repository.Create(new Item { Id = "a", Value = 1 });

        var replacement = new Item { Id = "a", Value = 2 };
        repository.Update(replacement);

        Assert.AreSame(replacement, repository.Read("a"));
        Assert.AreSame(replacement, repository.ReadAll().Single());
    }

    [TestMethod]
    public void Update_MissingElement_AddsIt()
    {
        var repository = new InMemoryRepository<Item>();
        var item = new Item { Id = "a", Value = 1 };

        repository.Update(item);

        Assert.AreSame(item, repository.Read("a"));
        Assert.AreSame(item, repository.ReadAll().Single());
    }

    [TestMethod]
    public void CreateDelete_ChangeSnapshotMembership()
    {
        var repository = new InMemoryRepository<Item>();
        var first = new Item { Id = "a" };
        var second = new Item { Id = "b" };
        repository.Create(first);
        repository.Create(second);
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, repository.ReadAll().Select(item => item.Id).ToArray());

        repository.Delete("a");

        Assert.IsNull(repository.Read("a"));
        Assert.AreEqual("b", repository.ReadAll().Single().Id);
    }
}
