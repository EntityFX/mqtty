using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EntityFX.MqttY.Web.App.Models;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class TwinValidationApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory factory;
    public TwinValidationApiTests(MqttYWebFactory factory)=>this.factory=factory;
    [Fact]
    public async Task ValidateAndExecutePinnedSingleBrokerThroughQueue()
    {
        var client=factory.CreateClient();
        var profile=TwinValidationFixture.Profile();
        var relative="profiles/twin-test.json";
        Directory.CreateDirectory(Path.Combine(factory.DataDirectory,"profiles"));
        await File.WriteAllBytesAsync(Path.Combine(factory.DataDirectory,relative),profile);
        (await client.PostAsJsonAsync("/api/presets/brokers",new BrokerPresetDto { Id="twin-test",Name="Twin test",
            BrokerType="Mosquitto",ProfilePath=relative })).EnsureSuccessStatusCode();
        object Request(bool validate)=>new { name="Проверка одиночной модели",brokerPresetId="twin-test",publishers=1,qos=1,
            warmupSeconds=.01,measurementSeconds=.1,drainSeconds=.1,tickSeconds=.00001,validateOnly=validate };
        var check=await client.PostAsJsonAsync("/api/twin-validations",Request(true));
        Assert.Equal(HttpStatusCode.OK,check.StatusCode);
        var launch=await client.PostAsJsonAsync("/api/twin-validations",Request(false));
        Assert.Equal(HttpStatusCode.Accepted,launch.StatusCode);
        var accepted=await launch.Content.ReadFromJsonAsync<JsonElement>();
        var id=accepted.GetProperty("id").GetString();
        JsonElement status=default;
        for(var i=0;i<100;i++)
        {
            status=await client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}");
            if(status.GetProperty("state").GetString() is "Completed" or "Failed")break;
            await Task.Delay(100);
        }
        Assert.Equal("Completed",status.GetProperty("state").GetString());
        Assert.Equal("twinValidation",status.GetProperty("engine").GetString());
        Assert.True(status.GetProperty("totalTicks").GetInt64()>0);
        var files=await client.GetFromJsonAsync<JsonElement>($"/api/simulations/{id}/results");
        Assert.True(files.TryGetProperty("summary.json",out _));
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/twin-validations",new {
            brokerPresetId="twin-test",publishers=5 })).StatusCode);
    }
}
