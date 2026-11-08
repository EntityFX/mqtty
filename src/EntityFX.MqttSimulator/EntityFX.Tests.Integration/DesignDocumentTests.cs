using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Design;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Mechanism)]
    public class DesignDocumentTests
    {
        [TestMethod]
        public void DesignDocument_RoundTripsNetworkGraphOptions()
        {
            var option = new NetworkGraphOption
            {
                RandomSeed = 42,
                EnableCounters = true,
                Ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(1) },
                NetworkTypes =
                {
                    ["eth"] = new NetworkOptions { Speed = 1_000, TransferTicks = 2, QueueCapacity = 100 }
                },
                Networks =
                {
                    ["net1"] = new NetworkNodeOption
                    {
                        Index = 0, NetworkType = "eth",
                        Links = new[] { new NetworkLinkOption { Network = "net2", W = 3 } }
                    }
                },
                Nodes =
                {
                    ["broker"] = new NodeOption
                    {
                        Type = NodeOptionType.Server, Protocol = "mqtt", Specification = "mqtt-server",
                        Network = "net1", Index = 1, SendTicks = 7, Broker = "mosquitto",
                        Additional = { ["role"] = new[] { "broker", "primary" } }
                    }
                }
            };

            var document = DesignDocument.FromNetworkGraphOption(option);
            var restored = document.ToNetworkGraphOption();

            Assert.AreEqual(42, restored.RandomSeed);
            Assert.AreEqual(1, restored.Networks.Count);
            Assert.AreEqual("net2", restored.Networks["net1"].Links[0].Network);
            Assert.AreEqual(3, restored.Networks["net1"].Links[0].W);
            Assert.AreEqual(NodeOptionType.Server, restored.Nodes["broker"].Type);
            Assert.AreEqual(1_000, restored.NetworkTypes["eth"].Speed);
            Assert.AreEqual(7, restored.Nodes["broker"].SendTicks);
            Assert.AreEqual("mosquitto", restored.Nodes["broker"].Broker);
            CollectionAssert.AreEqual(new[] { "broker", "primary" },
                restored.Nodes["broker"].Additional["role"]);
        }

        [TestMethod]
        public void LayoutEngine_IsDeterministicAndKeepsExplicitPositions()
        {
            var document = new DesignDocument(
                new[] { new DesignNetwork("net1", 0, "eth", Array.Empty<DesignLink>()) },
                new[]
                {
                    new DesignNode("n1", NodeOptionType.Client, "mqtt", "c", "net1", null, null, null,
                        null, null, new Dictionary<string, string[]>()),
                    new DesignNode("n2", NodeOptionType.Client, "mqtt", "c", "net1", null, null, null,
                        null, null, new Dictionary<string, string[]>())
                },
                new[] { new DesignNetworkType("eth", 1_000, 1, 100) },
                new TicksOptions(), true, 0,
                new GraphLayoutOption
                {
                    NodePositions = { ["n1"] = new GraphPositionOption { X = 7, Y = 9 } }
                });

            var layout = new GridDesignLayoutEngine();
            var first = layout.Apply(document);
            var second = layout.Apply(first);

            Assert.AreEqual(7, first.Layout!.NodePositions["n1"].X);
            Assert.IsTrue(first.Layout.NodePositions.ContainsKey("n2"));
            CollectionAssert.AreEqual(
                first.Layout.NodePositions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
                second.Layout!.NodePositions.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.AreEqual(first.Layout.NodePositions["n2"].X, second.Layout.NodePositions["n2"].X);
        }

        [TestMethod]
        public void DesignDocument_JsonRoundTrip()
        {
            var document = new DesignDocument(
                new[] { new DesignNetwork("net", 0, "eth", new[] { new DesignLink("other", 2) }) },
                Array.Empty<DesignNode>(),
                new[] { new DesignNetworkType("eth", 100, 1, 10) },
                new TicksOptions(), false, 5);

            var restored = DesignDocument.FromJson(document.ToJson());

            Assert.AreEqual("net", restored.Networks[0].Name);
            Assert.AreEqual(2, restored.Networks[0].Links[0].Weight);
            Assert.AreEqual(5, restored.RandomSeed);
        }

        [TestMethod]
        public void GraphMlReader_BuildsNetworksNodesAndLinks()
        {
            const string graphml = @"<graphml xmlns=""http://graphml.graphdrawing.org/xmlns"">
  <key id=""d0"" attr.name=""type"" attr.type=""string"" for=""node""/>
  <key id=""d1"" attr.name=""label"" attr.type=""string"" for=""node""/>
  <key id=""d2"" attr.name=""networkType"" attr.type=""string"" for=""node""/>
  <key id=""d3"" attr.name=""network"" attr.type=""string"" for=""node""/>
  <key id=""d4"" attr.name=""weight"" attr.type=""int"" for=""edge""/>
  <graph>
    <node id=""n0""><data key=""d0"">network</data><data key=""d1"">net1</data><data key=""d2"">eth</data></node>
    <node id=""n1""><data key=""d0"">network</data><data key=""d1"">net2</data><data key=""d2"">eth</data></node>
    <node id=""c0""><data key=""d0"">client</data><data key=""d1"">client1</data><data key=""d3"">net1</data></node>
    <edge source=""n0"" target=""n1""><data key=""d4"">3</data></edge>
  </graph>
</graphml>";

            var document = GraphMlDesignReader.Read(graphml);

            Assert.AreEqual(2, document.Networks.Count);
            Assert.AreEqual(1, document.Nodes.Count);
            Assert.AreEqual("net1", document.Nodes[0].Network);
            Assert.AreEqual("net2", document.Networks[0].Links[0].TargetNetwork);
            Assert.AreEqual(3, document.Networks[0].Links[0].Weight);
        }

        [TestMethod]
        public void GraphMlReader_UsesTypeDefaultsForServerAndClient()
        {
            const string graphml = @"<graphml xmlns=""http://graphml.graphdrawing.org/xmlns"">
  <key id=""d0"" attr.name=""type"" attr.type=""string"" for=""node""/>
  <key id=""d1"" attr.name=""label"" attr.type=""string"" for=""node""/>
  <graph>
    <node id=""n0""><data key=""d0"">network</data><data key=""d1"">net1</data></node>
    <node id=""s0""><data key=""d0"">server</data><data key=""d1"">broker1</data></node>
    <node id=""c0""><data key=""d0"">client</data><data key=""d1"">client1</data></node>
    <edge source=""s0"" target=""n0""/>
    <edge source=""c0"" target=""n0""/>
  </graph>
</graphml>";

            var document = GraphMlDesignReader.Read(graphml);
            var server = document.Nodes.Single(node => node.Name == "broker1");
            var client = document.Nodes.Single(node => node.Name == "client1");

            Assert.AreEqual(NodeOptionType.Server, server.Type);
            Assert.AreEqual("mqtt-broker", server.Specification);
            Assert.AreEqual("net1", server.Network);
            Assert.AreEqual("mqtt-client", client.Specification);
            Assert.AreEqual("net1", client.Network);
        }
    }
}
