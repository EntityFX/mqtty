using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.LoadBalancing;

namespace EntityFX.Tests.Integration;

[TestClass]
[DoNotParallelize]
[TestCategory(TestCategories.Regression)]
[TestCategory("LoadBalancing")]
public class LoadBalancingDemoTests
{
    [TestMethod]
    public void DefaultDemo_RunsInEmptyWorkingDirectoryWithoutCreatingFiles()
    {
        WithTemporaryRoot(root =>
        {
            var previous = Environment.CurrentDirectory;
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            try
            {
                Environment.CurrentDirectory = root;
                Assert.AreEqual(0, LoadBalancingCommand.Execute(new[] { "demo" }, stdout, stderr), stderr.ToString());
                Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length, "The default demo must neither require nor create files.");
            }
            finally { Environment.CurrentDirectory = previous; }
            var text = stdout.ToString();
            StringAssert.Contains(text, "идеальные модельные брокеры");
            StringAssert.Contains(text, "брокеров: 4");
            StringAssert.Contains(text, "publishers: 64");
            StringAssert.Contains(text, "[####################]");
            StringAssert.Contains(text, "Уникально получено: 100");
            StringAssert.Contains(text, "Файлы не записаны");
            Assert.AreEqual(string.Empty, stderr.ToString());
        });
    }

    [DataTestMethod]
    [DataRow("roundRobin", 0)]
    [DataRow("roundRobin", 1)]
    [DataRow("roundRobin", 2)]
    [DataRow("leastConnections", 0)]
    [DataRow("leastConnections", 1)]
    [DataRow("leastConnections", 2)]
    [DataRow("directBalanced", 0)]
    [DataRow("directBalanced", 1)]
    [DataRow("directBalanced", 2)]
    [DataRow("single", 0)]
    [DataRow("single", 1)]
    [DataRow("single", 2)]
    public void Demo_AllModesAndQos_UseRequestedAssignmentsAndDeliverLocally(string mode, int qos)
    {
        var args = new List<string> { "--mode", mode, "--publishers", "7", "--qos", qos.ToString(CultureInfo.InvariantCulture),
            "--rps", "1000", "--seconds", "0.02", "--seed", "43" };
        if (mode != "single") args.AddRange(new[] { "--brokers", "3" });
        var demo = DemoCommandOptions.Parse(args.ToArray());
        var input = demo.Prepare();
        Assert.IsTrue(demo.Experiment.Ideal);
        Assert.IsNull(demo.Experiment.BrokerProfilePath);
        Assert.IsNull(input.Profile);
        Assert.IsNull(demo.OutputDirectory);
        Assert.AreEqual(mode == "single" ? 1 : 3, demo.Experiment.BackendCount);
        var result = new LoadBalancingExperimentRunner().RunInMemory(input);
        Assert.AreEqual("completed", result.Status, result.Error);
        Assert.AreEqual(20L, result.ApiAttempts);
        Assert.AreEqual(20L, result.Delivery.UniqueDelivered);
        Assert.AreEqual(0L, result.Delivery.UnexpectedDeliveries);
        Assert.AreEqual(0L, result.Delivery.DuplicateDeliveries);
        CollectionAssert.AreEqual(mode == "single" ? new[] { 7 } : new[] { 3, 2, 2 }, result.Backends.Select(b => b.Publishers).ToArray());
        var text = new StringWriter();
        DemoConsoleView.WriteResult(text, input, result, null);
        StringAssert.Contains(text.ToString(), "Уникально получено: 20");
        StringAssert.Contains(text.ToString(), "проверка механизма");
        if (qos == 0)
        {
            Assert.IsNull(result.PublishP99Ms);
            StringAssert.Contains(text.ToString(), "QoS 0: задержка ACK неприменима");
        }
    }

    [DataTestMethod]
    [DataRow("--brokers", "0")]
    [DataRow("--brokers", "65")]
    [DataRow("--brokers", "2.5")]
    [DataRow("--publishers", "2")]
    [DataRow("--publishers", "4097")]
    [DataRow("--mode", "random")]
    [DataRow("--mode", "1")]
    [DataRow("--qos", "3")]
    [DataRow("--rps", "NaN")]
    [DataRow("--rps", "Infinity")]
    [DataRow("--rps", "-1")]
    [DataRow("--seconds", "0")]
    [DataRow("--seconds", "11")]
    [DataRow("--seconds", "0.00015")]
    [DataRow("--seconds", "0,1")]
    [DataRow("--config", "missing.json")]
    [DataRow("--profile", "missing.json")]
    [DataRow("--output", " ")]
    public void InvalidDemoArguments_FailBeforeCreatingAnOutput(string name, string value)
    {
        WithTemporaryRoot(root =>
        {
            var output = Path.Combine(root, "must-not-be-created");
            var args = name == "--output" ? new[] { "demo", name, value } : new[] { "demo", name, value, "--output", output };
            var error = new StringWriter();
            Assert.AreEqual(1, LoadBalancingCommand.Execute(args, new StringWriter(), error));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.ToString()));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
        });
    }

    [TestMethod]
    public void DemoParser_RejectsMissingDuplicateAndConflictingParameters()
    {
        foreach (var args in new[]
        {
            new[] { "--brokers" }, new[] { "--brokers", "--mode", "roundRobin" },
            new[] { "--brokers", "3", "--brokers", "4" }, new[] { "--mode", "single", "--brokers", "4" }
        }) Assert.AreEqual(1, LoadBalancingCommand.Execute(new[] { "demo" }.Concat(args).ToArray(), new StringWriter(), new StringWriter()));
    }

    [TestMethod]
    public void DemoHelp_DoesNotRunSimulation()
    {
        foreach (var args in new[] { new[] { "demo", "--help" }, new[] { "--help" } })
        {
            var output = new StringWriter();
            Assert.AreEqual(0, LoadBalancingCommand.Execute(args, output, new StringWriter()));
            StringAssert.Contains(output.ToString(), "--brokers");
            StringAssert.Contains(output.ToString(), "leastConnections");
            Assert.IsFalse(output.ToString().Contains("Статус:"));
        }
    }

    [TestMethod]
    public void DemoParser_UsesInvariantNumbersAcrossMachineCultures()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var options = DemoCommandOptions.Parse(new[] { "--seconds", "0.02", "--rps", "1000.5", "--mode", "LEASTCONNECTIONS" });
            Assert.AreEqual(.02, options.Experiment.MeasurementSeconds);
            Assert.AreEqual(1000.5, options.Experiment.OfferedRps);
            Assert.AreEqual(BalancingMode.LeastConnections, options.Experiment.Mode);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void OptionalOutput_SavesExactInputsAndReplaysTheSameScientificResult()
    {
        WithTemporaryRoot(root =>
        {
            var output = Path.Combine(root, "saved-demo");
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            Assert.AreEqual(0, LoadBalancingCommand.Execute(new[] { "demo", "--brokers", "3", "--publishers", "7",
                "--qos", "2", "--seconds", "0.02", "--output", output }, stdout, stderr), stderr.ToString());
            var replayInput = ExperimentInput.Load(Path.Combine(output, "run-config.json"), false).Single();
            Assert.IsTrue(replayInput.Options.Ideal);
            Assert.IsNull(replayInput.Profile);
            var repeated = new LoadBalancingExperimentRunner().RunInMemory(replayInput);
            Assert.AreEqual(File.ReadAllText(Path.Combine(output, "summary.json")), JsonSerializer.Serialize(repeated, ExperimentInput.Json));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "manifest.json")));
            Assert.AreEqual(JsonValueKind.Null, manifest.RootElement.GetProperty("profile").ValueKind);
            Assert.AreEqual("generated-from-demo-cli", manifest.RootElement.GetProperty("configuration").GetProperty("path").GetString());
            Assert.IsFalse(File.Exists(Path.Combine(output, "inputs", "profile.json")));
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "report.md")), "идеальная модель без реального профиля");
            using var registry = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "sha256.json")));
            foreach (var entry in registry.RootElement.EnumerateObject())
                Assert.AreEqual(entry.Value.GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, entry.Name)))).ToLowerInvariant());
            StringAssert.Contains(stdout.ToString(), "Пакет результатов:");
            Assert.AreEqual(1, LoadBalancingCommand.Execute(new[] { "demo", "--output", output }, new StringWriter(), new StringWriter()));
            Assert.AreEqual(File.ReadAllText(Path.Combine(output, "summary.json")), JsonSerializer.Serialize(repeated, ExperimentInput.Json), "Existing evidence must remain intact.");
        });
    }

    [TestMethod]
    public void NoPublicationWindow_RendersNotApplicableInsteadOfInventingZeroLoss()
    {
        var output = new StringWriter();
        Assert.AreEqual(0, LoadBalancingCommand.Execute(new[] { "demo", "--seconds", "0.0001", "--rps", "1" }, output, new StringWriter()));
        StringAssert.Contains(output.ToString(), "Запланировано: 0");
        StringAssert.Contains(output.ToString(), "Недоставка среди вызовов Publish: notApplicable");
    }

    private static void WithTemporaryRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-demo-test-" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(root); test(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
