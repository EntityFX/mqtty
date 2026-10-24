using EntityFX.MqttY.Web.App.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class TopologyGeneratorApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory _factory;

    public TopologyGeneratorApiTests(MqttYWebFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("line", 3)]
    [InlineData("ring", 4)]
    [InlineData("star", 3)]
    [InlineData("combined", 4)]
    public async Task Generate_ReturnsGraph(string kind, int size)
    {
        var client = _factory.CreateClient();
        var spec = new TopologySpecDto
        {
            Kind = kind,
            ChainLength = kind == "line" ? size : 3,
            Rays = 3,
            RingLength = kind == "ring" ? size : 4,
            BrokerPresetId = "mosquitto-default",
            ClientsPerBroker = 1,
            Engine = "relay"
        };

        var response = await client.PostAsJsonAsync("/api/topologies/generate", spec);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<GenerateResultDto>();
        Assert.NotNull(result);
        Assert.NotNull(result!.Graph);
        Assert.True(result.Graph!.Networks.Count > 0);
        Assert.True(result.Graph.Nodes.Count > 0);
    }

    [Fact]
    public async Task Generate_InvalidLine_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var spec = new TopologySpecDto { Kind = "line", ChainLength = 1, Engine = "relay" };

        var response = await client.PostAsJsonAsync("/api/topologies/generate", spec);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
