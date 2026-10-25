using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class BrokerProfilesApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory factory;
    public BrokerProfilesApiTests(MqttYWebFactory factory) => this.factory = factory;

    [Fact]
    public async Task ProfileEditCreatesValidatedCopyAndPreservesOriginal()
    {
        var client = factory.CreateClient();
        var assembly = typeof(BrokerBenchmarkRepository).Assembly;
        using var stream = assembly.GetManifestResourceStream("EntityFX.MqttY.Plugin.Mqtt.Data.broker-benchmark.v2.json")!;
        using var reader = new StreamReader(stream);
        var original = await reader.ReadToEndAsync();
        var id = "profile-copy-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(factory.DataDirectory, "profiles"));
        var source = Path.Combine(factory.DataDirectory, "profiles", id + ".json");
        await File.WriteAllTextAsync(source, original);
        (await client.PostAsJsonAsync("/api/presets/brokers", new BrokerPresetDto {
            Id = id, Name = "Profile copy test", BrokerType = "Mosquitto", ProfilePath = "profiles/" + id + ".json"
        })).EnsureSuccessStatusCode();
        var response = await client.PutAsJsonAsync($"/api/presets/brokers/{id}/profile", JsonSerializer.Deserialize<JsonElement>(original));
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<BrokerPresetDto>();
        Assert.StartsWith("profiles/custom/", saved!.ProfilePath);
        Assert.Equal(original, await File.ReadAllTextAsync(source));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/presets/brokers/{id}/profile")).StatusCode);
        var rejected = await client.PutAsJsonAsync($"/api/presets/brokers/{id}/profile", new { schemaVersion = 999 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var unchanged = await client.GetFromJsonAsync<BrokerPresetDto>($"/api/presets/brokers/{id}");
        Assert.Equal(saved.ProfilePath, unchanged!.ProfilePath);
    }

    [Fact]
    public async Task ProfileOutsideStoreReturnsBadRequest()
    {
        var client = factory.CreateClient();
        var id = "outside-profile-" + Guid.NewGuid().ToString("N");
        (await client.PostAsJsonAsync("/api/presets/brokers", new BrokerPresetDto {
            Id = id, Name = "Invalid path", BrokerType = "Mosquitto", ProfilePath = "../outside.json"
        })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/presets/brokers/{id}/profile")).StatusCode);
    }
}
