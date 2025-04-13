using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class CounterTests
    {
        [TestMethod]
        public void TestNetworkThroughput()
        {
            var payload = new byte[100];

            var nc = new NetworkCounters("nc1", new TicksOptions() { TickPeriod = TimeSpan.FromMilliseconds(0.1) });
            nc.CountInbound(new NetworkPacket("a", "b", NodeType.Client, NodeType.Client, payload, "tcp"));
            nc.Refresh(10);
        }
    }
}