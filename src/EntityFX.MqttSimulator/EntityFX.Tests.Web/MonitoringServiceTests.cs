using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Web.App.Services;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class MonitoringServiceTests
{
    [Fact]
    public void Update_StoresSnapshotAndNode()
    {
        var monitoring = new MonitoringService();
        var progress = new EngineProgress
        {
            TotalTicks = 100,
            Step = 5,
            Errors = 0,
            Nodes = new[]
            {
                new NodeStatusSnapshot
                {
                    Id = "broker-0",
                    Name = "broker-0",
                    Type = "Server",
                    Specification = "mqtt-broker",
                    Network = "net-0",
                    Connected = true,
                    QueueLength = 2,
                    Counters = new Dictionary<string, long> { ["published"] = 10 }
                }
            }
        };

        monitoring.Update("sim-1", progress);

        var snapshot = monitoring.GetSnapshot("sim-1");
        Assert.NotNull(snapshot);
        Assert.Equal(100, snapshot!.TotalTicks);
        Assert.Single(snapshot.Nodes);

        var node = monitoring.GetNode("sim-1", "broker-0");
        Assert.NotNull(node);
        Assert.Equal("mqtt-broker", node!.Specification);
        Assert.Equal(10, node.Counters["published"]);
    }

    [Fact]
    public void GetNode_Unknown_ReturnsNull()
    {
        var monitoring = new MonitoringService();
        Assert.Null(monitoring.GetNode("sim-1", "missing"));
    }

    [Fact]
    public void SetState_RaisesStateChanged()
    {
        var monitoring = new MonitoringService();
        string? raisedId = null;
        string? raisedState = null;
        monitoring.StateChanged += (id, status) => { raisedId = id; raisedState = status.State; };

        monitoring.SetState("sim-1", "Completed");

        Assert.Equal("sim-1", raisedId);
        Assert.Equal("Completed", raisedState);
    }
}
