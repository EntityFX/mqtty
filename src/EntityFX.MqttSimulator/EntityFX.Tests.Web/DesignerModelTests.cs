using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Designer.Models;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class DesignerModelTests
{
    [Fact]
    public void Layout_RoundTripsThroughGraphOption()
    {
        var model = new NetworkDesignModel
        {
            Layout = new GraphLayoutOption
            {
                ZoomLevel = 1.5,
                NetworkPositions = new Dictionary<string, GraphPositionOption>
                {
                    ["net"] = new() { X = 10, Y = 20 }
                },
                NodePositions = new Dictionary<string, GraphPositionOption>
                {
                    ["node"] = new() { X = 30, Y = 40 }
                }
            }
        };

        var option = model.ToNetworkGraphOption();
        Assert.NotNull(option.Layout);
        Assert.Equal(1.5, option.Layout!.ZoomLevel);

        var roundTrip = NetworkDesignModel.FromNetworkGraphOption(option);
        Assert.NotNull(roundTrip.Layout);
        Assert.Equal(1.5, roundTrip.Layout!.ZoomLevel);
        Assert.Equal(10, roundTrip.Layout.NetworkPositions["net"].X);
        Assert.Equal(20, roundTrip.Layout.NetworkPositions["net"].Y);
        Assert.Equal(30, roundTrip.Layout.NodePositions["node"].X);
        Assert.Equal(40, roundTrip.Layout.NodePositions["node"].Y);
    }

    [Fact]
    public void NetworkTypeMapping_PreservesQueueCapacityAndSendTicks()
    {
        var model = new NetworkDesignModel();
        model.Ticks.OutgoingWaitTicks = 7;
        model.NetworkTypes.Add(new NetworkTypeModel
        {
            Name = "eth",
            Speed = 1_000_000,
            RefreshTicks = 3,
            SendTicks = 7,
            QueueSize = 1234
        });
        model.Networks.Add(new NetworkNodeDesignModel { Name = "net", NetworkType = "eth" });

        var option = model.ToNetworkGraphOption();

        Assert.Equal(1234, option.NetworkTypes["eth"].QueueCapacity);
        Assert.Equal(3, option.NetworkTypes["eth"].TransferTicks);
        Assert.Equal(1_000_000, option.NetworkTypes["eth"].Speed);

        var roundTrip = NetworkDesignModel.FromNetworkGraphOption(option).NetworkTypes.Single();
        Assert.Equal(1234, roundTrip.QueueSize);
        Assert.Equal(7, roundTrip.SendTicks);
        Assert.Equal(3, roundTrip.RefreshTicks);
    }

    [Fact]
    public void NetworkTypeMapping_DefaultsQueueCapacityWhenUnset()
    {
        var model = new NetworkDesignModel();
        model.NetworkTypes.Add(new NetworkTypeModel { Name = "eth", Speed = 1_000_000, RefreshTicks = 3 });

        var option = model.ToNetworkGraphOption();

        Assert.Equal(50000, option.NetworkTypes["eth"].QueueCapacity);
    }
}
