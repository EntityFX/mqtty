using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Results;

namespace EntityFX.Tests.Integration.Results;

[TestClass]
public class JsonResultWriterTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    [TestMethod]
    public void IdenticalWrites_ProduceIdenticalJsonAndSeal()
    {
        InDirectory(root =>
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            var writer = JsonResultWriter.Default;
            writer.CreateOutput(first);
            writer.CreateOutput(second);

            var value = new
            {
                status = "completed",
                counts = new Dictionary<string, long> { ["b"] = 2, ["a"] = 1 },
                publications = new[] { 1, 2, 3 }
            };

            writer.WriteJson(Path.Combine(first, "summary.json"), value, Json);
            writer.WriteJson(Path.Combine(second, "summary.json"), value, Json);
            writer.WriteText(Path.Combine(first, "report.md"), "# report\n");
            writer.WriteText(Path.Combine(second, "report.md"), "# report\n");

            writer.Seal(first, Json);
            writer.Seal(second, Json);

            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first, "summary.json")),
                File.ReadAllBytes(Path.Combine(second, "summary.json")));
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first, "sha256.json")),
                File.ReadAllBytes(Path.Combine(second, "sha256.json")));

            using var seal = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(first, "sha256.json")));
            var keys = seal.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            CollectionAssert.AreEqual(new[] { "report.md", "summary.json" }, keys,
                "Seal entries must be sorted by relative path.");
        });
    }

    [TestMethod]
    public void FailedWrite_DoesNotReplaceExistingArtifact()
    {
        InDirectory(root =>
        {
            var writer = JsonResultWriter.Default;
            var path = Path.Combine(root, "summary.json");
            writer.WriteJson(path, new { value = 1 }, Json);
            var original = File.ReadAllBytes(path);

            Assert.ThrowsException<InvalidOperationException>(() =>
                writer.WriteJson(path, BrokenRows(), Json));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));
        });
    }

    private static IEnumerable<int> BrokenRows()
    {
        yield return 1;
        throw new InvalidOperationException("interrupted");
    }

    private static void InDirectory(Action<string> test)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
            "mqtty-result-writer-tests-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
