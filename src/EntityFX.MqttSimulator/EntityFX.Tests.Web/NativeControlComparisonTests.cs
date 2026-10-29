using System.Text.Json;
using EntityFX.MqttY.Engines.TwinValidation;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class NativeControlComparisonTests
{
    [Fact]
    public void LossUsesCompletedIdsRetainsDrainAndExcludesWarmup()
    {
        var root=Path.Combine(Path.GetTempPath(),"native-cohort-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"phases.json"),"""{"measurementStartSeconds":1,"measurementEndSeconds":2,"drainEndSeconds":3,"completed":true}""");
        File.WriteAllLines(Path.Combine(root,"publishes.ndjson"),new[] {
            """{"id":"warmup","event":"attempt","elapsedSeconds":0.5}""",
            """{"id":"warmup","event":"completed","elapsedSeconds":0.6,"latencyMs":100}""",
            """{"id":"a","event":"attempt","elapsedSeconds":1.1}""",
            """{"id":"a","event":"completed","elapsedSeconds":1.2,"latencyMs":100}""",
            """{"id":"b","event":"attempt","elapsedSeconds":1.9}""",
            """{"id":"b","event":"completed","elapsedSeconds":2.1,"latencyMs":200}""",
            """{"id":"c","event":"attempt","elapsedSeconds":1.6}""",
            """{"id":"c","event":"failed","elapsedSeconds":1.7}""" });
        File.WriteAllLines(Path.Combine(root,"deliveries.ndjson"),new[] {
            """{"id":"warmup","elapsedSeconds":0.7}""","""{"id":"a","elapsedSeconds":1.3}""","""{"id":"a","elapsedSeconds":1.4}""" });
        var reference=NativeControlReference.Parse(System.Text.Encoding.UTF8.GetBytes(NativeControlApiTests.Reference(TwinValidationFixture.Profile(true)).ToJsonString()));
        var result=reference.Compare(root,new string('a',64));var cohort=result.GetProperty("cohort");var metrics=result.GetProperty("metrics");
        Assert.Equal(3,cohort.GetProperty("attempted").GetInt32());Assert.Equal(2,cohort.GetProperty("completed").GetInt32());
        Assert.Equal(1,cohort.GetProperty("failed").GetInt32());Assert.Equal(1,cohort.GetProperty("completedIdsDelivered").GetInt32());
        Assert.Equal(.5,metrics.GetProperty("loss").GetProperty("simulated").GetDouble(),12);
        Assert.Equal(1d/3,metrics.GetProperty("failure").GetProperty("simulated").GetDouble(),12);
        Assert.Equal(2,metrics.GetProperty("rps").GetProperty("simulated").GetDouble(),12);
        Assert.Equal(200,metrics.GetProperty("p99").GetProperty("simulated").GetDouble(),12);
        Assert.Equal(.15,metrics.GetProperty("rps").GetProperty("limit").GetDouble());
        Assert.Equal(.20,metrics.GetProperty("p99").GetProperty("limit").GetDouble());
        Assert.Equal(.05,metrics.GetProperty("failure").GetProperty("limit").GetDouble());
        Assert.Equal(.05,metrics.GetProperty("loss").GetProperty("limit").GetDouble());
        Assert.False(result.GetProperty("allCriteriaPassed").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOrPendingModelDoesNotReceiveAccuracyPass(bool pending)
    {
        var root=Path.Combine(Path.GetTempPath(),"native-empty-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"phases.json"),"""{"measurementStartSeconds":1,"measurementEndSeconds":2,"drainEndSeconds":3,"completed":true}""");
        File.WriteAllText(Path.Combine(root,"publishes.ndjson"),pending?"""{"id":"pending","event":"attempt","elapsedSeconds":1.1}""":"");
        File.WriteAllText(Path.Combine(root,"deliveries.ndjson"),"");
        var reference=NativeControlReference.Parse(System.Text.Encoding.UTF8.GetBytes(NativeControlApiTests.Reference(TwinValidationFixture.Profile(true)).ToJsonString()));
        var result=reference.Compare(root,new string('a',64));
        Assert.Equal("notApplicable",result.GetProperty("metrics").GetProperty("loss").GetProperty("status").GetString());
        Assert.Equal("notApplicable",result.GetProperty("metrics").GetProperty("p99").GetProperty("status").GetString());
        Assert.Equal(!pending,result.GetProperty("cohort").GetProperty("outcomesSettled").GetBoolean());
        Assert.False(result.GetProperty("allCriteriaPassed").GetBoolean());
    }
}
