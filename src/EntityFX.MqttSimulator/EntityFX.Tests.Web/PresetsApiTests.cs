using EntityFX.MqttY.Web.App.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class PresetsApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory _factory;

    public PresetsApiTests(MqttYWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBrokers_ReturnsSeedPresets()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/presets/brokers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var brokers = await response.Content.ReadFromJsonAsync<List<BrokerPresetDto>>();
        Assert.NotNull(brokers);
        Assert.Contains(brokers!, b => b.BrokerType == "Mosquitto");
        Assert.Contains(brokers!, b => b.BrokerType == "Aedes");
    }

    [Fact]
    public async Task GetTopologies_ReturnsSeedPresets()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/presets/topologies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var topologies = await response.Content.ReadFromJsonAsync<List<TopologyPresetDto>>();
        Assert.NotNull(topologies);
        Assert.Contains(topologies!, t => t.Kind == "line");
        Assert.Contains(topologies!, t => t.Kind == "ring");
        Assert.Contains(topologies!, t => t.Kind == "star");
        Assert.Contains(topologies!, t => t.Kind == "combined");
    }

    [Fact]
    public async Task CreateAndDeleteBrokerPreset()
    {
        var client = _factory.CreateClient();
        var preset = new BrokerPresetDto
        {
            Name = "Test Broker",
            BrokerType = "TestType",
            ProfilePath = "profiles/TestType/broker-calibration.v3.json"
        };

        var create = await client.PostAsJsonAsync("/api/presets/brokers", preset);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<BrokerPresetDto>();
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));

        var delete = await client.DeleteAsync($"/api/presets/brokers/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }
}
