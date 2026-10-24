using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Federation;

namespace EntityFX.Tests.Integration.Results;

[TestClass]
public class FederationResultWriterTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void StreamedJson_PreservesScientificBytesAndAtomicReplacement()
    {
        InDirectory(root =>
        {
            var path = Path.Combine(root, "summary.json");
            var value = new
            {
                mode = FederationMode.Federation, text = "Проверка", latency = 1.2, optional = (double?)null,
                publications = new[] { new FederationPublication(1, 0, 0, 0, 3, true, new byte[] { 0, 32, 255 }) },
                transmissions = new[] { new TransmissionResult(4, "federator0", 0, 1, 1, true, "forward", "federation/routing/0/0", 3, "hash", 27, true) },
                histogram = new Dictionary<long, long> { [4] = 1 }
            };
            var expected = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, FederationInput.Json));
            FederationResultWriter.Json(path, value);
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(path));
            Assert.IsFalse(File.Exists(path + ".tmp"));

            Assert.ThrowsException<InvalidOperationException>(() => FederationResultWriter.Json(path, BrokenRows()));
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(path), "An incomplete write must not replace the sealed result.");
        });
    }

    [TestMethod]
    public void LargeJson_DoesNotAllocateAWholeOutputString()
    {
        InDirectory(root =>
        {
            var path = Path.Combine(root, "transmissions.json");
            var row = new string('x', 2048);
            FederationResultWriter.Json(path, Enumerable.Repeat(row, 1)); // Warm serializer metadata and buffers.
            var start = GC.GetAllocatedBytesForCurrentThread();
            FederationResultWriter.Json(path, Enumerable.Repeat(row, 5000));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            var length = new FileInfo(path).Length;
            TestContext.WriteLine($"Streamed output: {length} bytes; managed allocation during write: {allocated} bytes.");
            Assert.IsTrue(length > 10_000_000);
            Assert.IsTrue(allocated < length / 2, $"Allocated {allocated} bytes for {length} output bytes; output must be streamed.");
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            Assert.AreEqual(5000, document.RootElement.GetArrayLength());
        });
    }

    [TestMethod]
    public void Seal_HashesLargeEvidenceWithoutReadingItIntoAnArray()
    {
        InDirectory(root =>
        {
            var path = Path.Combine(root, "transmissions.json");
            using (var stream = File.Create(path))
            {
                var block = new byte[64 * 1024];
                for (var i = 0; i < 128; i++) stream.Write(block);
            }
            FederationResultWriter.Seal(root);
            var start = GC.GetAllocatedBytesForCurrentThread();
            FederationResultWriter.Seal(root);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            TestContext.WriteLine($"Evidence file: {new FileInfo(path).Length} bytes; managed allocation during seal: {allocated} bytes.");
            Assert.IsTrue(allocated < 1_000_000, $"Sealing allocated {allocated} bytes for an 8 MiB file.");
            using var input = File.OpenRead(path);
            using var sha256 = SHA256.Create();
            var expected = Convert.ToHexString(sha256.ComputeHash(input)).ToLowerInvariant();
            var seals = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "sha256.json")))!;
            Assert.AreEqual(expected, seals["transmissions.json"]);
            Assert.IsFalse(seals.ContainsKey("sha256.json"));
        });
    }

    private static IEnumerable<int> BrokenRows()
    {
        yield return 1;
        throw new InvalidOperationException("Interrupted evidence enumeration");
    }

    private static void InDirectory(Action<string> test)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mqtty-writer-tests-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
}
