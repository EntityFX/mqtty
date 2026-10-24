using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EntityFX.MqttY.Web.App.Services;
using Xunit;
namespace EntityFX.Tests.Web;

public sealed class ScientificExecutionApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory _factory;
    public ScientificExecutionApiTests(MqttYWebFactory factory) { _factory = factory; }

    [Theory]
    [InlineData("relay", "InProcess")]
    [InlineData("federation", "InProcess")]
    [InlineData("loadBalancing", "InProcess")]
    [InlineData("relay", "OutOfProcess")]
    [InlineData("federation", "OutOfProcess")]
    [InlineData("loadBalancing", "OutOfProcess")]
    public async Task RealRunnersDeliverExpectedCohortAndSealOutput(string engine, string executionMode)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/simulations", Request(engine, executionMode));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = job.GetProperty("id").GetString()!;
        var terminal = await UntilTerminal(client, id);
        Assert.True(terminal.GetProperty("state").GetString() == "Completed", terminal.ToString());
        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}/results/summary.json");
        Assert.Equal("completed", summary.GetProperty("status").GetString());
        Assert.Equal(20, summary.GetProperty("apiAttempts").GetInt64());
        if (engine == "loadBalancing") Assert.Equal(20, summary.GetProperty("delivery").GetProperty("uniqueDelivered").GetInt64());
        else { Assert.Equal(40, summary.GetProperty("uniqueDeliveries").GetInt64()); Assert.Equal(0, summary.GetProperty("duplicateDeliveries").GetInt64()); }
        var output = Path.Combine(_factory.DataDirectory, "simulations", id);
        using var seal = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "sha256.json")));
        foreach (var entry in seal.RootElement.EnumerateObject())
            Assert.Equal(entry.Value.GetString(), Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(output, entry.Name)))).ToLowerInvariant());
    }

    [Fact]
    public async Task MissingExternalProfileAndEditedGraphAreRejectedBeforeQueueing()
    {
        var client = _factory.CreateClient();
        var missing = await client.PostAsJsonAsync("/api/simulations", new { topologyId = "line-3-mosquitto", engine = "relay" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var graph = await client.PostAsJsonAsync("/api/simulations", new { engine = "federation", graph = new { nodes = Array.Empty<object>() } });
        Assert.Equal(HttpStatusCode.BadRequest, graph.StatusCode);
    }

    [Fact]
    public async Task WorkerWallLimitBecomesTimedOutAndPreservesJobReceipt()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/simulations", new
        {
            engine = "federation", spec = new { kind = "line", chainLength = 2, brokerPresetId = "ideal-demo", clientsPerBroker = 1,
                warmupTicks = 0, measurementTicks = 3000000, drainTimeoutTicks = 10000, offeredRps = 100 },
            runSettings = new { executionMode = "OutOfProcess", timeLimit = "00:00:00.050", parallel = false }
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var terminal = await UntilTerminal(client, id);
        Assert.Equal("TimedOut", terminal.GetProperty("state").GetString());
        var reopened = new JobRegistry(new JsonFileStore(_factory.DataDirectory));
        Assert.Equal("TimedOut", reopened.Get(id)!.State);
        Assert.NotNull(reopened.Get(id)!.Error);
    }

    [Fact]
    public async Task CancelledQueuedJobIsNeverExecutedAndRegistrySurvivesReload()
    {
        var client = _factory.CreateClient();
        var first = await client.PostAsJsonAsync("/api/simulations", Request("federation", "OutOfProcess", longRun: true));
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var second = await client.PostAsJsonAsync("/api/simulations", Request("relay", "OutOfProcess"));
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        await client.PostAsync($"/api/simulations/{secondId}/stop", null);
        await client.PostAsync($"/api/simulations/{firstId}/stop", null);
        await UntilTerminal(client, firstId);
        var terminal = await UntilTerminal(client, secondId);
        Assert.Equal("Cancelled", terminal.GetProperty("state").GetString());
        Assert.False(Directory.Exists(Path.Combine(_factory.DataDirectory, "simulations", secondId)));
        var reopened = new JobRegistry(new JsonFileStore(_factory.DataDirectory));
        Assert.Equal("Cancelled", reopened.Get(secondId)!.State);
    }

    private static object Request(string engine, string mode, bool longRun = false) => new
    {
        engine, spec = new { kind = "line", chainLength = 2, brokerPresetId = "ideal-demo", clientsPerBroker = 1,
            warmupTicks = 10, measurementTicks = longRun ? 3000000 : 200, drainTimeoutTicks = 10000,
            qos = 1, offeredRps = longRun ? 100 : 1000 },
        runSettings = new { executionMode = mode, timeLimit = "00:01:00", parallel = false }
    };
    private static async Task<JsonElement> UntilTerminal(HttpClient client, string id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var status = await client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}", deadline.Token);
            if (status.GetProperty("state").GetString() is "Completed" or "Failed" or "Cancelled" or "TimedOut") return status;
            await Task.Delay(25, deadline.Token);
        }
    }
}
