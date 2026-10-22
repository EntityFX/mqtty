using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Regression)]
public class FederationTests
{
    private static FederationOptions Small(FederationMode mode = FederationMode.Federation, int qos = 1) =>
        new() { Mode = mode, Publishers = 6, WarmupSeconds = 0, MeasurementSeconds = .006, Qos = qos };

    [DataTestMethod]
    [DataRow(FederationMode.Single, 0)] [DataRow(FederationMode.Single, 1)] [DataRow(FederationMode.Single, 2)]
    [DataRow(FederationMode.Relay, 0)] [DataRow(FederationMode.Relay, 1)] [DataRow(FederationMode.Relay, 2)]
    [DataRow(FederationMode.Federation, 0)] [DataRow(FederationMode.Federation, 1)] [DataRow(FederationMode.Federation, 2)]
    public void AllModes_DeliverToEverySubscriber_AndCountOnlySourceCompletions(FederationMode mode, int qos)
    {
        var s = new FederationRunner().Run(FederationInput.Prepare(Small(mode, qos)));
        Assert.AreEqual("completed", s.Status, s.Error);
        Assert.AreEqual(6L, s.ApiAttempts); Assert.AreEqual(6L, s.SourceCompleted);
        Assert.AreEqual(18L, s.ExpectedDeliveries); Assert.AreEqual(18L, s.UniqueDeliveries);
        Assert.AreEqual(6L, s.FullyDeliveredPublications);
        Assert.AreEqual(0d, s.DeliveryLoss); Assert.AreEqual(0L, s.DuplicateDeliveries);
        Assert.AreEqual(0L, s.UnknownDeliveries); Assert.AreEqual(0L, s.UnexpectedDeliveries);
        Assert.AreEqual(1000d, s.SourceCompletedRps); Assert.AreEqual(3000d, s.UniqueGoodput);
        if (qos == 0) Assert.IsNull(s.PublishP99Ms); else Assert.IsTrue(s.PublishP99Ms > 0);
        Assert.AreEqual(qos == 0 ? 0 : 6, s.PublishHistogramTicks.Values.Sum());
        if (mode == FederationMode.Single) Assert.AreEqual(0L, s.ForwardAttempts);
        else Assert.IsTrue(s.ForwardAttempts >= 12);
        if (mode == FederationMode.Federation) Assert.IsTrue(s.SuppressedDuplicates > 0);
    }

    [DataTestMethod]
    [DataRow("line", 0)] [DataRow("line", 1)] [DataRow("line", 2)]
    [DataRow("custom", 0)] [DataRow("custom", 1)] [DataRow("custom", 2)]
    public void LineAndDiamond_RemoteDeliveryIsFinite(string topology, int qos)
    {
        var o = Small(qos: qos) with { Brokers = 4, Publishers = 4, MeasurementSeconds = .004, Topology = topology,
            Links = topology == "custom" ? new[] { new SiteLink(0,1),new SiteLink(0,2),new SiteLink(1,3),new SiteLink(2,3) } : null };
        var s = new FederationRunner().Run(FederationInput.Prepare(o));
        Assert.AreEqual("completed", s.Status, s.Error); Assert.AreEqual(16L, s.UniqueDeliveries);
        Assert.AreEqual(0L, s.DuplicateDeliveries); Assert.IsTrue(s.ForwardAttempts <= 32);
    }

    [TestMethod]
    public void Relay_ConnectsEveryBrokerToEveryOtherBroker_WithoutReexportingImports()
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-federation-tests-" + Guid.NewGuid());
        try
        {
            foreach (var shape in new[] { "line", "ring", "custom" })
            foreach (var qos in new[] { 0, 1, 2 })
            {
                var o = Small(FederationMode.Relay, qos) with
                {
                    Brokers = 4, Publishers = 4, MeasurementSeconds = .004, Topology = shape,
                    Links = shape == "custom" ? new[] { new SiteLink(0,1), new SiteLink(0,2), new SiteLink(1,3), new SiteLink(2,3) } : null
                };
                var input = FederationInput.Prepare(o);
                var ledger = new FederationDeliveryLedger(o);
                using (var topology = FederationTopologyBuilder.Build(input, ledger, new TransmissionLedger(ledger)))
                    Assert.AreEqual(4, topology.Relays.Count, "One MqttRelay application per broker.");
                var output = Path.Combine(root, shape + qos);
                var summary = new FederationRunner().Run(input, output);
                Assert.AreEqual("completed", summary.Status, summary.Error);
                Assert.AreEqual(16L, summary.UniqueDeliveries);
                Assert.AreEqual(0L, summary.DuplicateDeliveries);
                Assert.AreEqual(12L, summary.ForwardAttempts);
                var publications = JsonSerializer.Deserialize<FederationPublication[]>(File.ReadAllText(Path.Combine(output, "publications.json")), FederationInput.Json)!.ToDictionary(p => p.Id);
                var tx = JsonSerializer.Deserialize<TransmissionResult[]>(File.ReadAllText(Path.Combine(output, "transmissions.json")), FederationInput.Json)!;
                var forwards = tx.Where(t => t.Role == "forward" && t.Measurement).ToArray();
                foreach (var source in publications.Values)
                {
                    var actual = forwards.Where(t => t.PublicationId == source.Id).ToArray();
                    Assert.AreEqual(3, actual.Length);
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, 4).Where(s => s != source.Origin).ToArray(), actual.Select(t => t.Target).ToArray());
                    Assert.IsTrue(actual.All(t => t.Site == source.Origin && t.Accepted && t.Topic == FederationTopics.Relay(source.Origin)),
                        "Every export must originate at the source site; importing sites must never reexport it.");
                }
                var mmd = File.ReadAllText(Path.Combine(output, "topology-logical.mmd"));
                Assert.AreEqual(12, mmd.Split('\n').Count(line => line.Contains(" -->")));
                StringAssert.Contains(mmd, "S0 -->|\"источник 0\"| S3");
                var graph = File.ReadAllText(Path.Combine(output, "topology.graphml"));
                Assert.ThrowsException<InvalidDataException>(() => FederationDiagrams.Verify(graph.Replace(">relay0relayfrom0to3<", ">missing<"), o));
            }
        }
        finally
        {
            var absolute = Path.GetFullPath(root); var temp = Path.GetFullPath(Path.GetTempPath());
            if (absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("mqtty-federation-tests-", StringComparison.Ordinal)) Directory.Delete(absolute, true);
        }
    }

    [DataTestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public void Relay_WithoutSubscribers_StillExportsOnceToEveryPeer(int qos)
    {
        var summary = new FederationRunner().Run(FederationInput.Prepare(Small(FederationMode.Relay, qos) with { ReceiverSites = Array.Empty<int>() }));
        Assert.AreEqual("completed", summary.Status, summary.Error);
        Assert.AreEqual(12L, summary.ForwardAttempts);
        Assert.AreEqual(0L, summary.ExpectedDeliveries);
        Assert.IsNull(summary.DeliveryLoss);
    }

    [TestMethod]
    public void TenBrokerRelay_UsesAnEmptyTransitNetwork_ForEveryPhysicalPath()
    {
        foreach (var qos in new[] { 0, 1, 2 })
        {
            var output = Path.Combine(Path.GetTempPath(), "mqtty-federation-tests-" + Guid.NewGuid());
            try
            {
                var options = Small(FederationMode.Relay, qos) with
                { Brokers = 10, Publishers = 10, Topology = "star", MeasurementSeconds = .01, WarmupSeconds = .01 };
                var summary = new FederationRunner().Run(FederationInput.Prepare(options), output);
                Assert.AreEqual("completed", summary.Status, summary.Error);
                Assert.AreEqual(10L, summary.SourceCompleted);
                Assert.AreEqual(100L, summary.UniqueDeliveries);
                Assert.AreEqual(90L, summary.ForwardAttempts);
                Assert.AreEqual(0L, summary.DuplicateDeliveries);
                var evidence = JsonSerializer.Deserialize<TransitNetworkEvidence>(File.ReadAllText(Path.Combine(output, "network-evidence.json")), FederationInput.Json)!;
                Assert.AreEqual(11, evidence.NetworkCount);
                Assert.AreEqual(10, evidence.BrokerCount);
                Assert.IsFalse(evidence.HasAttachedEndpoints);
                Assert.AreEqual(90, evidence.AvailablePaths.Length);
                foreach (var route in evidence.AvailablePaths)
                    CollectionAssert.AreEqual(new[] { "site" + route.Source, "transit", "site" + route.Target }, route.Networks);
                var delta = evidence.MeasurementAndDrainCounters!;
                Assert.IsTrue(evidence.BeforeMeasurement!.Counters["TransferPackets"] > 0, "The warmup baseline must be captured, not counted as measurement.");
                Assert.IsTrue(delta["TransferPackets"] >= 90);
                Assert.AreEqual(delta["InboundPackets"], delta["OutboundPackets"]);
                Assert.AreEqual(delta["TransferPackets"], delta["OutboundPackets"]);
                Assert.AreEqual(delta["Inbound"], delta["Outbound"]);
                Assert.AreEqual(0L, delta["Refused"]);
                Assert.AreEqual(0L, evidence.Final.QueuedPackets);
                var physical = File.ReadAllText(Path.Combine(output, "topology-physical.mmd"));
                Assert.AreEqual(10, physical.Split('\n').Count(line => line.Contains("<-->")));
                StringAssert.Contains(physical, "S10[\"transit:");
                var logical = File.ReadAllText(Path.Combine(output, "topology-logical.mmd"));
                Assert.AreEqual(90, logical.Split('\n').Count(line => line.Contains(" -->")));
                XDocument.Parse(File.ReadAllText(Path.Combine(output, "topology-logical.svg")));
                FederationDiagrams.Verify(File.ReadAllText(Path.Combine(output, "topology.graphml")), options);
            }
            finally
            {
                var absolute = Path.GetFullPath(output); var temp = Path.GetFullPath(Path.GetTempPath());
                if (absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("mqtty-federation-tests-", StringComparison.Ordinal)) Directory.Delete(absolute, true);
            }
        }
    }

    [DataTestMethod]
    [DataRow(FederationMode.Single, 0)] [DataRow(FederationMode.Single, 1)] [DataRow(FederationMode.Single, 2)]
    [DataRow(FederationMode.Federation, 0)] [DataRow(FederationMode.Federation, 1)] [DataRow(FederationMode.Federation, 2)]
    public void TransitTopology_PreservesOtherModes(FederationMode mode, int qos)
    {
        var options = Small(mode, qos) with { Brokers = 4, Publishers = 4, Topology = "star", MeasurementSeconds = .004 };
        var summary = new FederationRunner().Run(FederationInput.Prepare(options));
        Assert.AreEqual("completed", summary.Status, summary.Error);
        Assert.AreEqual(16L, summary.UniqueDeliveries);
        Assert.AreEqual(0L, summary.DuplicateDeliveries);
    }

    [TestMethod]
    public void TransitNetwork_AddsModelledLatency_AndCliAcceptsStar()
    {
        var options = Small(FederationMode.Relay) with { Brokers = 2, Publishers = 2, MeasurementSeconds = .002, Topology = "line" };
        var direct = new FederationRunner().Run(FederationInput.Prepare(options));
        var transit = new FederationRunner().Run(FederationInput.Prepare(options with { Topology = "star" }));
        Assert.AreEqual("completed", direct.Status, direct.Error);
        Assert.AreEqual("completed", transit.Status, transit.Error);
        Assert.IsTrue(transit.ReceiverP99Ms > direct.ReceiverP99Ms, "The transit must be simulated, not just drawn.");
        var demo = FederationDemoOptions.Parse(new[] { "--brokers", "10", "--topology", "star", "--mode", "relay" });
        Assert.AreEqual(11, FederationGraph.NetworkCount(demo.Options));
        Assert.ThrowsException<InvalidDataException>(() => (options with { Topology = "star", Links = new[] { new SiteLink(0, 1) } }).Validate());
    }

    [TestMethod]
    public void HopLimitAndQueueOverflow_AreVisibleLoss_NotInfiniteExecution()
    {
        var limited = new FederationRunner().Run(FederationInput.Prepare(Small() with {
            Brokers=4,Publishers=4,Topology="line",HopLimit=1,MeasurementSeconds=.004 }));
        Assert.AreEqual("completed",limited.Status,limited.Error);
        Assert.IsTrue(limited.HopLimitDrops>0);Assert.IsTrue(limited.UniqueDeliveries<limited.ExpectedDeliveries);
        var overflow = new FederationRunner().Run(FederationInput.Prepare(Small(qos:2) with {
            Publishers=12,OfferedRps=20000,QueueCapacity=1,MeasurementSeconds=.002 }));
        Assert.AreEqual("completed",overflow.Status,overflow.Error);Assert.IsTrue(overflow.QueueDrops>0);
    }

    [TestMethod]
    public void NoSubscribers_HasNoDeliveryDenominator()
    {
        var s=new FederationRunner().Run(FederationInput.Prepare(Small() with { ReceiverSites=Array.Empty<int>() }));
        Assert.AreEqual("completed",s.Status,s.Error);Assert.AreEqual(0L,s.ExpectedDeliveries);
        Assert.IsNull(s.DeliveryLoss);Assert.IsNull(s.FullyDeliveredPublications);Assert.IsNull(s.ReceiverP99Ms);
    }

    [TestMethod]
    public void SingleLocalReceiver_PreservesPayloadAndDoesNotConfuseWarmupOrRepeatedIds()
    {
        var o=Small() with { ReceiverSites=new[]{2} };var ledger=new FederationDeliveryLedger(o);
        var warm=ledger.Create(0,10,false);var a=ledger.Create(0,20,true);var b=ledger.Create(1,20,true);
        Assert.AreNotEqual(a.Id,b.Id);
        ledger.Receive(2,FederationTopics.Delivered(warm.Origin),warm.Payload,100);
        ledger.Receive(2,FederationTopics.Delivered(a.Origin),a.Payload,100);
        ledger.Receive(2,FederationTopics.Delivered(a.Origin),a.Payload,101);
        var changed=(byte[])b.Payload.Clone();changed[15]^=1;
        ledger.Receive(2,FederationTopics.Delivered(b.Origin),changed,101);
        ledger.Receive(2,FederationTopics.Delivered(b.Origin),b.Payload,102);
        Assert.AreEqual(2L,ledger.Unique);Assert.AreEqual(1L,ledger.WarmupDeliveries);
        Assert.AreEqual(1L,ledger.UnknownDeliveries);Assert.AreEqual(1L,ledger.Snapshot()[0].Duplicates);
        Assert.IsTrue(a.Payload.SequenceEqual(ledger.Find(a.Payload)!.Payload));
    }

    [TestMethod]
    public void SameSequenceAtDifferentOrigins_IsNotADuplicate()
    {
        var o=Small();var ledger=new FederationDeliveryLedger(o);var traffic=new TransmissionLedger(ledger);
        using var topology=FederationTopologyBuilder.Build(FederationInput.Prepare(o),ledger,traffic);
        var a=ledger.Create(0,0,true);var b=ledger.Create(1,0,true);
        Assert.AreEqual(a.Sequence,b.Sequence);
        topology.Federators[0].Receive(new(FederationTopics.Input(0),a.Payload,MqttQos.AtLeastOnce,"broker0000"));
        // Source identity in the envelope must match both origin and payload, not just an integer sequence.
        Assert.ThrowsException<InvalidDataException>(()=>topology.Federators[0].Receive(new(
            FederationTopics.Routing(b.Origin,b.Sequence+1,1,1),b.Payload,MqttQos.AtLeastOnce,"broker0000")));
        topology.Federators[0].Receive(new(FederationTopics.Routing(b.Origin,b.Sequence,1,1),b.Payload,MqttQos.AtLeastOnce,"broker0000"));
        Assert.AreEqual(2L,topology.Federators[0].CacheEntries);Assert.AreEqual(0L,topology.Federators[0].SuppressedDuplicates);
    }

    [TestMethod]
    public void OptInPublisherMetrics_ExcludeServicePublishersFailuresAndOldGeneration()
    {
        var m=new BrokerMeasurement();m.TrackPublishers(new[]{"publisher"});m.Reset(10);
        var source=m.BeginAttempt("publisher",MqttQos.AtLeastOnce,1,1,10);m.Admit(source);m.CompletePublisher("publisher",MqttQos.AtLeastOnce,1,15);
        var service=m.BeginAttempt("service",MqttQos.AtLeastOnce,1,2,10);m.Admit(service);m.CompletePublisher("service",MqttQos.AtLeastOnce,1,20);
        var rejected=m.BeginAttempt("publisher",MqttQos.AtLeastOnce,2,3,10);m.FailPublish(rejected);
        var snapshot=m.PublisherSnapshot("publisher",MqttQos.AtLeastOnce,20,TimeSpan.FromMilliseconds(.1));
        Assert.AreEqual(2L,snapshot.Attempted);Assert.AreEqual(1L,snapshot.Completed);Assert.AreEqual(1L,snapshot.PublishFailed);
        Assert.AreEqual(.5,snapshot.LatencyP99Ms);Assert.AreEqual(2L,m.Snapshot(20,TimeSpan.FromMilliseconds(.1)).ByQos[MqttQos.AtLeastOnce].Completed);
        var late=m.BeginAttempt("publisher",MqttQos.AtLeastOnce,3,4,10);m.Admit(late);m.Reset(30);m.CompletePublisher("publisher",MqttQos.AtLeastOnce,3,35);
        Assert.AreEqual(0L,m.PublisherSnapshot("publisher",MqttQos.AtLeastOnce,40,TimeSpan.FromMilliseconds(.1)).Completed);
    }

    [TestMethod]
    public void Validation_RejectsCacheBudgetUnknownFieldsDisconnectedAndRepeatedEdges()
    {
        Assert.ThrowsException<InvalidDataException>(()=>(Small() with {CacheCapacity=1}).Validate());
        Assert.ThrowsException<InvalidDataException>(()=>(Small() with {Topology="custom",Links=new[]{new SiteLink(0,1)}}).Validate());
        Assert.ThrowsException<InvalidDataException>(()=>(Small() with {Topology="custom",Links=new[]{new SiteLink(0,1),new SiteLink(1,0)}}).Validate());
        var path=Path.Combine(Path.GetTempPath(),"federation-config-"+Guid.NewGuid()+".json");
        File.WriteAllText(path,"{\"publushers\":12}");
        try {Assert.ThrowsException<InvalidDataException>(()=>FederationInput.Load(path));}
        finally {File.Delete(path);}
    }

    [TestMethod]
    public void SourceQuantiles_UseMergedHistogram_NotMeanOfBackendQuantiles()
    {
        var merged=FederationStatistics.Merge(new IReadOnlyDictionary<long,long>[] {
            new Dictionary<long,long>{{1,99}},new Dictionary<long,long>{{100,1}} });
        Assert.AreEqual(.1,FederationStatistics.Quantile(merged,.99,.1));
        Assert.AreEqual(10d,FederationStatistics.Quantile(merged,1,.1));
    }

    [TestMethod]
    public void PreparedProfile_RequiresExactN_AndRetainsItsSnapshot()
    {
        // A controlled synthetic profile tests lookup rules; it is not experimental evidence.
        var points = Enum.GetValues<MqttQos>().ToDictionary(q => q, q => new CalibratedMqttQosProfile(new[] {
            new CalibratedMqttQosSample(16, 1000, 0, 0, q == MqttQos.AtMostOnce ? null : new LatencyQuantiles(0,0,0,0,0,0)) }));
        var profile = new MqttBrokerProfile { BrokerType = "Mosquitto", RequireObservedPublisherCount = true,
            MessageSizes = new Dictionary<int, MqttMessageSizeProfile> { [16] = new(16, points) } };
        var options = Small() with { Brokers = 3, Publishers = 48, Ideal = false, BrokerProfilePath = "unused-synthetic-input" };
        var bytes = new byte[] { 1, 2, 3 };
        var prepared = new FederationInput(options, Array.Empty<byte>(), bytes, profile, "synthetic");
        var next = prepared.WithOptions(options with { Seed = 43 });
        Assert.AreSame(bytes, next.ProfileBytes); Assert.AreSame(profile, next.Profile);
        Assert.ThrowsException<InvalidDataException>(() => prepared.WithOptions(options with { Mode = FederationMode.Single }));
        Assert.ThrowsException<InvalidDataException>(() => prepared.WithOptions(options with { Ideal = true, BrokerProfilePath = null }));
    }

    [TestMethod]
    public void TechnicalFailure_SavesReasonAndSealedPartialFiles()
    {
        var output = Path.Combine(Path.GetTempPath(), "mqtty-federation-tests-" + Guid.NewGuid());
        try
        {
            var result = new FederationRunner().Run(FederationInput.Prepare(Small() with { DrainTimeoutSeconds = .0001 }), output);
            Assert.AreEqual("failed", result.Status);
            Assert.IsTrue(File.Exists(Path.Combine(output, "sha256.json")));
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "report.md")), "connect timed out");
            Assert.IsTrue(File.Exists(Path.Combine(output, "delivery-receipts.json")));
        }
        finally
        {
            var absolute = Path.GetFullPath(output); var temp = Path.GetFullPath(Path.GetTempPath());
            if (absolute.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("mqtty-federation-tests-", StringComparison.Ordinal)) Directory.Delete(absolute, true);
        }
    }

    [TestMethod]
    public void SavedRun_ReplaysExactly_SealsPayloadsAndRejectsMovedClients()
    {
        var root=Path.Combine(Path.GetTempPath(),"mqtty-federation-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var first=Path.Combine(root,"first");var second=Path.Combine(root,"second");
            var input=FederationInput.Prepare(Small(FederationMode.Relay) with { WarmupSeconds=.002 });
            var a=new FederationRunner().Run(input,first);
            Assert.AreEqual("completed",a.Status,a.Error);
            var b=new FederationRunner().Run(FederationInput.Load(Path.Combine(first,"run-config.json")),second);
            Assert.AreEqual("completed",b.Status,b.Error);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(first,"summary.json")),File.ReadAllBytes(Path.Combine(second,"summary.json")));
            foreach(var entry in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(first,"sha256.json")))!)
                Assert.AreEqual(entry.Value,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(first,entry.Key)))).ToLowerInvariant());
            var publications=JsonSerializer.Deserialize<FederationPublication[]>(File.ReadAllText(Path.Combine(first,"publications.json")),FederationInput.Json)!;
            var tx=JsonSerializer.Deserialize<TransmissionResult[]>(File.ReadAllText(Path.Combine(first,"transmissions.json")),FederationInput.Json)!;
            Assert.IsTrue(tx.Any(t=>t.Role=="forward"));
            Assert.IsTrue(tx.All(t=>t.PayloadBytes==16&&t.TopicPayloadBytes>16));
            Assert.AreEqual(a.ApiAttempts,publications.LongCount(p=>p.Measurement));
            Assert.ThrowsException<IOException>(()=>new FederationRunner().Run(input,first));
            var graph=File.ReadAllText(Path.Combine(first,"topology.graphml"));FederationDiagrams.Verify(graph,input.Options);
            Assert.ThrowsException<InvalidDataException>(()=>FederationDiagrams.Verify(graph.Replace(">site0<",">wrong<"),input.Options));
            XDocument.Parse(File.ReadAllText(Path.Combine(first,"topology-logical.svg")));
        }
        finally
        {
            var absolute=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath());
            if(absolute.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(absolute).StartsWith("mqtty-federation-tests-",StringComparison.Ordinal))Directory.Delete(absolute,true);
        }
    }

    [TestMethod]
    public void DrainTimeout_RetainsPartialEvidenceAndReason()
    {
        var o=Small() with { DrainTimeoutSeconds=.0001 };
        var s=new FederationRunner().Run(FederationInput.Prepare(o));
        Assert.AreEqual("failed",s.Status);StringAssert.Contains(s.Error!,"connect timed out");
        Assert.IsTrue(s.Phases.Any(p=>p.Status=="failed"));Assert.IsNull(s.SourceCompletedRps);
    }

    [TestMethod]
    public void Demo_CliValidatesArgumentsAndCanRunWithoutFiles()
    {
        using var stdout=new StringWriter();using var stderr=new StringWriter();
        Assert.AreEqual(1,FederationCommand.Execute(new[]{"demo","--bogus","1"},stdout,stderr));
        Assert.AreEqual(0,FederationCommand.Execute(new[]{"demo","--mode","single","--seconds","0.001"},stdout,stderr));
        StringAssert.Contains(stdout.ToString(),"subscriber@2");
    }
}
