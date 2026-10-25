using System.Net.Http.Json;
using System.Text.Json;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class SimulationSearchApiTests : IClassFixture<MqttYWebFactory>
{
    private readonly MqttYWebFactory factory;
    public SimulationSearchApiTests(MqttYWebFactory factory) => this.factory = factory;

    [Fact]
    public async Task SearchPagesJobsAndRetainsTimeNameAndTick()
    {
        var client = factory.CreateClient();
        var registry = factory.Services.GetRequiredService<IJobRegistry>();
        var prefix = "campaign-" + Guid.NewGuid().ToString("N");
        var start = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        for (var i = 0; i < 53; i++)
        {
            var job = new SimulationJob { Name = prefix, State = "Completed", Engine = "federation", StartedAt = start.AddSeconds(i) };
            job.RunOptions.Graph.Ticks.TickPeriod = TimeSpan.FromMilliseconds(.25);
            registry.Add(job);
        }
        var response = await client.GetFromJsonAsync<JsonElement>($"/api/simulations/search?q={prefix}&state=Completed&engine=federation&page=2&pageSize=25");
        Assert.Equal(53, response.GetProperty("total").GetInt32());
        Assert.Equal(3, response.GetProperty("pages").GetInt32());
        var items = response.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(25, items.Length);
        Assert.All(items, item => { Assert.Equal(prefix, item.GetProperty("name").GetString()); Assert.Equal(.25, item.GetProperty("tickMilliseconds").GetDouble()); });
        Assert.Equal(start.AddSeconds(27), items[0].GetProperty("startedAt").GetDateTimeOffset());
        var filtered = await client.GetFromJsonAsync<JsonElement>($"/api/simulations/search?q={prefix}&from=2026-01-01T12:00:10Z&to=2026-01-01T12:00:14Z&page=999&pageSize=2");
        Assert.Equal(5, filtered.GetProperty("total").GetInt32());
        Assert.Equal(3, filtered.GetProperty("page").GetInt32());
        Assert.Single(filtered.GetProperty("items").EnumerateArray());
    }
}
