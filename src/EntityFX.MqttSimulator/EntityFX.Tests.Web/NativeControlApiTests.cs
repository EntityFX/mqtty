using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class NativeControlApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory factory;
    public NativeControlApiTests(MqttYWebFactory factory) => this.factory=factory;

    internal static JsonObject Reference(byte[] profile)
    {
        var runs=new JsonArray();
        for(var i=1;i<=3;i++) runs.Add(new JsonObject {
            ["id"]="control.Mosquitto.r"+i,["stage"]="control",["measurementStartedUtc"]="2021-01-01T00:00:00Z",
            ["publicationsSha256"]=new string((char)('0'+i),64),["deliveriesSha256"]=new string('a',64),["phasesSha256"]=new string('b',64),
            ["attempted"]=100,["completed"]=90,["failed"]=10,["completedIdsDelivered"]=81,["pending"]=0,
            ["measurementSeconds"]=.1,["p99PublishMs"]=i });
        return new JsonObject { ["schemaVersion"]=1,["artifactType"]="mqtt-native-control-v1",["name"]="Отдельный натурный контроль",
            ["broker"]="Mosquitto",["profileSha256"]=TwinValidationFixture.Hash(profile),["calibrationCampaignId"]="synthetic-web-test",
            ["controlCampaignId"]="synthetic-control-"+Guid.NewGuid().ToString("N"),["profileFrozenUtc"]="2020-01-01T00:00:00Z",
            ["programSha256"]=new string('a',64),["auditSha256"]=new string('b',64),
            ["workload"]=new JsonObject { ["payloadBytes"]=16,["qos"]=1,["publishers"]=16,["subscribers"]=1,
                ["warmupSeconds"]=.01,["measurementSeconds"]=.1,["drainSeconds"]=.1,["loadPolicy"]="closedLoopPerPublisher" },["runs"]=runs };
    }

    private async Task<(HttpClient Client,string Preset,byte[] Profile)> Setup()
    {
        var client=factory.CreateClient();var profile=TwinValidationFixture.Profile(true);
        var id="native-"+Guid.NewGuid().ToString("N");var relative="profiles/"+id+".json";
        Directory.CreateDirectory(Path.Combine(factory.DataDirectory,"profiles"));
        await File.WriteAllBytesAsync(Path.Combine(factory.DataDirectory,relative),profile);
        (await client.PostAsJsonAsync("/api/presets/brokers",new {id,name=id,brokerType="Mosquitto",profilePath=relative})).EnsureSuccessStatusCode();
        return (client,id,profile);
    }
    private static object Request(string preset,string reference,bool validate=true,double measurement=.1) => new {
        name="Проверка натурного контроля",brokerPresetId=preset,nativeControlReferenceId=reference,publishers=16,qos=1,seed=42,
        warmupSeconds=.01,measurementSeconds=measurement,drainSeconds=.1,tickSeconds=.00001,timeLimitSeconds=60,validateOnly=validate };

    [Fact]
    public async Task ImportExecuteAndPinSeparateNativeReference()
    {
        var (client,preset,profile)=await Setup();
        var bytes=Encoding.UTF8.GetBytes(Reference(profile).ToJsonString());
        var imported=await client.PostAsync("/api/twin-validations/references",new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK,imported.StatusCode);
        var saved=await imported.Content.ReadFromJsonAsync<JsonElement>();var id=saved.GetProperty("id").GetString()!;
        Assert.Equal(TwinValidationFixture.Hash(bytes),id);
        var listed=await client.GetFromJsonAsync<JsonElement>("/api/twin-validations/references");
        Assert.Contains(listed.EnumerateArray(),r=>r.GetProperty("id").GetString()==id);
        var valid=await client.PostAsJsonAsync("/api/twin-validations",Request(preset,id));
        Assert.Equal(HttpStatusCode.OK,valid.StatusCode);
        Assert.Equal("native control after profile freeze",(await valid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("referenceScope").GetString());
        var launched=await client.PostAsJsonAsync("/api/twin-validations",Request(preset,id,false));
        Assert.Equal(HttpStatusCode.Accepted,launched.StatusCode);
        var jobId=(await launched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        JsonElement status=default;
        for(var i=0;i<150;i++) { status=await client.GetFromJsonAsync<JsonElement>("/api/simulations/"+jobId);if(status.GetProperty("state").GetString() is "Completed" or "Failed")break;await Task.Delay(100); }
        Assert.Equal("Completed",status.GetProperty("state").GetString());
        var comparison=await client.GetFromJsonAsync<JsonElement>($"/api/simulations/{jobId}/results/native-control-comparison.json");
        Assert.Equal(id,comparison.GetProperty("referenceSha256").GetString());
        Assert.Equal(3,comparison.GetProperty("referenceRuns").GetInt32());
        Assert.Equal(900,comparison.GetProperty("metrics").GetProperty("rps").GetProperty("reference").GetDouble(),8);
        Assert.Equal(2,comparison.GetProperty("metrics").GetProperty("p99").GetProperty("reference").GetDouble(),8);
        var snapshot=await client.GetByteArrayAsync($"/api/simulations/{jobId}/results/native-control-snapshot.json");
        Assert.Equal(bytes,snapshot);
        var job=factory.Services.GetRequiredService<IJobRegistry>().Get(jobId)!;
        var config=job.RunOptions.NativeConfiguration!.Value;
        var pinned=config.EnumerateObject().Single(p=>p.Name.Equals("nativeControlReferencePath",StringComparison.OrdinalIgnoreCase)).Value.GetString()!;
        await File.AppendAllTextAsync(Path.Combine(factory.DataDirectory,"native-controls",id+".json")," ");
        Assert.True(NativeExperimentAdapter.Run(job.RunOptions,null,validateOnly:true).Success);
        await File.AppendAllTextAsync(pinned," ");
        Assert.Throws<InvalidDataException>(()=>NativeExperimentAdapter.Run(job.RunOptions,null,validateOnly:true));
    }

    [Theory]
    [InlineData("profileSha256")]
    [InlineData("measurementSeconds")]
    [InlineData("calibrationEvents")]
    public async Task RejectIncompatibleReferenceBeforeEnqueue(string changed)
    {
        var (client,preset,profile)=await Setup();var reference=Reference(profile);
        if(changed=="profileSha256")reference["profileSha256"]=new string('f',64);
        if(changed=="calibrationEvents")reference["runs"]![0]!["publicationsSha256"]=new string('d',64);
        var imported=await client.PostAsJsonAsync("/api/twin-validations/references",reference);imported.EnsureSuccessStatusCode();
        var id=(await imported.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var response=await client.PostAsJsonAsync("/api/twin-validations",Request(preset,id,true,changed=="measurementSeconds"?.2:.1));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }

    [Theory]
    [InlineData("sameCampaign")]
    [InlineData("beforeFreeze")]
    [InlineData("pending")]
    [InlineData("badCounts")]
    [InlineData("duplicateRun")]
    public async Task RejectMalformedOrCalibrationReference(string changed)
    {
        var client=factory.CreateClient();var reference=Reference(TwinValidationFixture.Profile(true));
        if(changed=="sameCampaign")reference["controlCampaignId"]=reference["calibrationCampaignId"]!.GetValue<string>();
        if(changed=="beforeFreeze")reference["runs"]![0]!["measurementStartedUtc"]="2019-01-01T00:00:00Z";
        if(changed=="pending")reference["runs"]![0]!["pending"]=1;
        if(changed=="badCounts")reference["runs"]![0]!["completedIdsDelivered"]=91;
        if(changed=="duplicateRun")reference["runs"]![1]!["id"]=reference["runs"]![0]!["id"]!.GetValue<string>();
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/twin-validations/references",reference)).StatusCode);
    }
}
