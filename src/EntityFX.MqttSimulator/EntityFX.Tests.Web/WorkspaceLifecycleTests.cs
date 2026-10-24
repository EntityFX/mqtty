using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace EntityFX.Tests.Web;

public sealed class WorkspaceLifecycleTests : IClassFixture<MqttYWebFactory>
{
    private readonly HttpClient _client;
    public WorkspaceLifecycleTests(MqttYWebFactory factory) { _client = factory.CreateClient(); }
    private static object Spec => new { kind="line", chainLength=2, brokerPresetId="ideal-demo", clientsPerBroker=1,
        qos=1, warmupTicks=10, measurementTicks=200, drainTimeoutTicks=10000, offeredRps=1000 };
    [Fact]
    public async Task SavedGeneratedTopologyRunsAfterMovingNodesButRejectsStructuralEdits()
    {
        var generated=await _client.PostAsJsonAsync("/api/topologies/generate",Spec);
        generated.EnsureSuccessStatusCode();
        var root=JsonNode.Parse(await generated.Content.ReadAsStringAsync())!;
        var graph=root["graph"]!.DeepClone();
        graph["nodes"]![0]!["x"]=777;
        graph["nodes"]![0]!["name"]="Переименованный брокер";
        var saved=await _client.PostAsJsonAsync("/api/topologies",new {name="Проверка редактора", graph, spec=Spec});
        saved.EnsureSuccessStatusCode();
        var id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        var run=await _client.PostAsJsonAsync("/api/simulations",new {topologyId=id,engine="relay",runSettings=new {executionMode="InProcess"}});
        Assert.True(run.StatusCode == HttpStatusCode.Accepted, await run.Content.ReadAsStringAsync());
        var job=(await run.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var terminal=await Wait(job);
        Assert.Equal("Completed",terminal.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK,(await _client.GetAsync($"/api/simulations/{job}/manifest")).StatusCode);
        graph["nodes"]!.AsArray().RemoveAt(0);
        saved=await _client.PostAsJsonAsync("/api/topologies",new {name="Изменённый граф",graph,spec=Spec});
        id=(await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
        run=await _client.PostAsJsonAsync("/api/simulations",new {topologyId=id,engine="relay"});
        Assert.Equal(HttpStatusCode.BadRequest,run.StatusCode);
    }
    [Fact]
    public async Task PausePreservesWorkerAndStopThenRestartCreatesNewOutput()
    {
        var response=await _client.PostAsJsonAsync("/api/simulations",new {engine="federation",spec=new {
            kind="line",chainLength=2,brokerPresetId="ideal-demo",clientsPerBroker=1,qos=1,
            warmupTicks=10,measurementTicks=3000000,drainTimeoutTicks=10000,offeredRps=100},
            runSettings=new {executionMode="OutOfProcess",timeLimit="00:01:00"}});
        response.EnsureSuccessStatusCode();
        var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        try {
            JsonElement running=default;
            for(var i=0;i<200;i++) {running=await _client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}");if(running.GetProperty("processId").ValueKind==JsonValueKind.Number)break;await Task.Delay(50);}
            var pid=running.GetProperty("processId").GetInt32();
            var paused=await _client.PostAsync($"/api/simulations/{id}/pause",null);
            Assert.Equal(HttpStatusCode.OK,paused.StatusCode);
            await Task.Delay(150);
            var status=await _client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}");
            Assert.Equal("Paused",status.GetProperty("state").GetString());
            Assert.Equal(pid,status.GetProperty("processId").GetInt32());
            (await _client.PostAsync($"/api/simulations/{id}/resume",null)).EnsureSuccessStatusCode();
            status=await _client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}");
            Assert.Equal("Running",status.GetProperty("state").GetString());
            Assert.Equal(pid,status.GetProperty("processId").GetInt32());
        } finally { await _client.PostAsync($"/api/simulations/{id}/stop",null); }
        var stopped=await Wait(id);
        Assert.Equal("Cancelled",stopped.GetProperty("state").GetString());
        var restart=await _client.PostAsync($"/api/simulations/{id}/restart",null);
        Assert.Equal(HttpStatusCode.Accepted,restart.StatusCode);
        var copy=await restart.Content.ReadFromJsonAsync<JsonElement>();
        var copyId=copy.GetProperty("id").GetString()!;
        Assert.NotEqual(id,copyId);
        Assert.NotEqual(stopped.GetProperty("outputDir").GetString(),copy.GetProperty("outputDir").GetString());
        await _client.PostAsync($"/api/simulations/{copyId}/stop",null);
        await Wait(copyId);
    }
    private async Task<JsonElement> Wait(string id) {
        for(var i=0;i<400;i++) {var job=await _client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}");if(job.GetProperty("state").GetString() is "Completed" or "Failed" or "Cancelled" or "TimedOut")return job;await Task.Delay(50);}
        throw new TimeoutException(id);
    }
}
