using EntityFX.MqttY.Contracts.Options;
using Models = EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class ConfigurationMapperTests
{
    private readonly ConfigurationMapperService _mapper = new();

    [Fact]
    public void RoundTrip_GraphDto_ToNetworkGraphOption_ToGraphDto()
    {
        var graph = new Models.GraphDto
        {
            Networks = { new Models.NetworkDto { Id = "net-0", NetworkType = "ethernet" } },
            Nodes =
            {
                new Models.NodeDto { Id = "broker-0", Type = "Server", Specification = "mqtt-broker", Network = "net-0" },
                new Models.NodeDto { Id = "client-0", Type = "Client", Specification = "mqtt-client", Network = "net-0" }
            },
            Links = { new Models.LinkDto { From = "net-0", To = "net-0" } }
        };

        var option = _mapper.ToNetworkGraphOption(graph);
        Assert.NotNull(option);
        Assert.Single(option.Networks);
        Assert.Equal(2, option.Nodes.Count);
        Assert.Equal(NodeOptionType.Server, option.Nodes["broker-0"].Type);

        var back = _mapper.ToGraphDto(option);
        Assert.Single(back.Networks);
        Assert.Equal(2, back.Nodes.Count);
        Assert.Equal("mqtt-broker", back.Nodes[0].Specification);
    }

    [Fact]
    public void ToRunOptions_CarriesEngineAndPhases()
    {
        var option = new NetworkGraphOption();
        var spec = new Models.TopologySpecDto
        {
            Engine = "federation",
            WarmupTicks = 10,
            MeasurementTicks = 20,
            DrainTimeoutTicks = 5
        };

        var runOptions = _mapper.ToRunOptions(option, "federation", spec);
        Assert.Equal("federation", runOptions.Engine);
        Assert.Equal(10, runOptions.WarmupTicks);
        Assert.Equal(20, runOptions.MeasurementTicks);
        Assert.Equal(5, runOptions.DrainTimeoutTicks);
    }
}
