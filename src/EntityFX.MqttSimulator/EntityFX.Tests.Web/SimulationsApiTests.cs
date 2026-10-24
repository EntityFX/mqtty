using EntityFX.MqttY.Web.App.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class SimulationsApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory _factory;

    public SimulationsApiTests(MqttYWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateSimulation_ReturnsAccepted()
    {
        var client = _factory.CreateClient();
        var body = new
        {
            engine = "relay", spec = new { kind = "line", chainLength = 2, brokerPresetId = "ideal-demo", clientsPerBroker = 1,
                warmupTicks = 10, measurementTicks = 200, drainTimeoutTicks = 10000, offeredRps = 1000 },
            runSettings = new { timeLimit = "00:01:00", parallel = false, executionMode = "InProcess" }
        };

        var response = await client.PostAsJsonAsync("/api/simulations", body);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        var status = await response.Content.ReadFromJsonAsync<SimulationStatusDto>();
        Assert.NotNull(status);
        Assert.False(string.IsNullOrWhiteSpace(status!.Id));
        Assert.Equal("relay", status.Engine);
    }

    [Fact]
    public async Task GetSimulations_ReturnsList()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/simulations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<List<SimulationStatusDto>>();
        Assert.NotNull(list);
    }

    [Fact]
    public async Task StopUnknownSimulation_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/simulations/unknown/stop", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
