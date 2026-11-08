using EntityFX.MqttY.Designer.Models;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class DesignerModelTests
{
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
