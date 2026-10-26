using System.Text.Json;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class TwinValidationAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameInputsMatchDirectRunnerAndRetainCohortAndPhases(bool singlePoint)
    {
        var root = Path.Combine(Path.GetTempPath(), "twin-web-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var bytes = TwinValidationFixture.Profile(singlePoint);
        var publishers = singlePoint ? 16 : 1;
        var profile = Path.Combine(root,"profile.json"); File.WriteAllBytes(profile,bytes);
        var hash = TwinValidationFixture.Hash(bytes);
        var config = new { broker="Mosquitto", publishers, payloadBytes=16, qos=1, seed=42,
            warmupSeconds=.01, measurementSeconds=.1, drainSeconds=.1, tickSeconds=.00001,
            profilePath=profile, profileSha256=hash, runtimeIdentity="synthetic-test", observeFullDrainWindow=true };
        var run = new SimulationRunOptions { Engine="twinValidation", ExpectedProfileSha256=hash,
            NativeConfiguration=JsonSerializer.SerializeToElement(config) };
        var output=Path.Combine(root,"web");
        var result=NativeExperimentAdapter.Run(run,output);
        Assert.True(result.Success,result.Error);
        var profiles = new BrokerProfileSet(new[]{profile});
        var direct = new MqttRelayExperimentRunner(new TicksOptions { TickPeriod=TimeSpan.FromSeconds(.00001),
            OutgoingWaitTicks=1, ReceiveWaitPeriod=TimeSpan.FromSeconds(1), CounterHistoryDepth=1 },
            new NetworkOptions { NetworkType="eth", TransferTicks=1, CapacityBytesPerSecond=100_000_000,
                ThroughputWindowTicks=1000, QueueCapacity=100_000 },profiles).Run(new MqttRelayExperimentOptions {
                RecordPublicationCohort=true, ObserveFullDrainWindow=true, TopologyMode=ExperimentTopologyMode.BrokerFidelity,
                Brokers=1, NetworkLength=1, ClientsPerBroker=publishers, ProfileClientCountMode=ProfileClientCountMode.Publishers,
                PublishQos=MqttQos.AtLeastOnce, SubscribeQos=MqttQos.AtLeastOnce, PayloadBytes=16,
                BrokerAssignmentMode=MqttBrokerAssignmentMode.Mosquitto, WarmupTicks=1000, MeasurementTicks=10000,
                DrainTimeoutTicks=10000, OfferedRps=publishers/.00001, RandomSeed=42, LoadMode=PublishLoadMode.ClosedLoopPerPublisher,
                TransportTiming=TransportTimingMode.EmpiricalServiceBudget, EnableCounters=false, Parallel=false, RefreshStrategy=0
            },Path.Combine(root,"direct"),new("synthetic-test","synthetic-test",hash) { BrokerProfiles=profiles.Artifacts });
        foreach(var file in new[]{"summary.json","latency-histogram.json","publishes.ndjson","deliveries.ndjson","connections.ndjson","subscriptions.ndjson"})
        {
            var directFile=Path.Combine(direct.RunDirectory,file);
            Assert.True(File.Exists(directFile),file);
            Assert.Equal(File.ReadAllBytes(directFile),File.ReadAllBytes(Path.Combine(output,file)));
        }
        Assert.Equal(File.ReadAllBytes(Path.Combine(direct.RunDirectory,"summary.json")),File.ReadAllBytes(Path.Combine(output,"summary.json")));
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"experiment-manifest.json")));
        Assert.Equal("brokerFidelity",manifest.RootElement.GetProperty("options").GetProperty("topologyMode").GetString());
        Assert.Equal(manifest.RootElement.GetProperty("phases").EnumerateArray().Last().GetProperty("endTick").GetInt64(),result.TotalTicks);
        Assert.True(result.TotalTicks>=21000);
        Assert.True(result.TotalSteps>0);
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(output,"profile-snapshot.json")));
        using var seals=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"sha256.json")));
        foreach(var seal in seals.RootElement.EnumerateObject())
            Assert.Equal(seal.Value.GetString(),TwinValidationFixture.Hash(File.ReadAllBytes(Path.Combine(output,seal.Name))));
        if (singlePoint)
        {
            var unsupported = JsonSerializer.SerializeToNode(config)!;
            unsupported["publishers"] = 1;
            var other = new SimulationRunOptions { Engine="twinValidation", ExpectedProfileSha256=hash,
                NativeConfiguration=JsonSerializer.SerializeToElement(unsupported) };
            Assert.Throws<InvalidDataException>(()=>NativeExperimentAdapter.Run(other,null,validateOnly:true));
        }
        File.AppendAllText(profile," ");
        Assert.Throws<InvalidDataException>(()=>NativeExperimentAdapter.Run(run,null,validateOnly:true));
    }
}
