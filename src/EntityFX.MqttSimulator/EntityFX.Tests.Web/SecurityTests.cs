using EntityFX.MqttY.Web.App.Security;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EntityFX.Tests.Web;

public class SecurityTests
{
    [Fact]
    public void JsonStore_RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-security-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonFileStore(root);
            Assert.Throws<InvalidOperationException>(() => store.Resolve("../outside.json"));
            Assert.Throws<InvalidOperationException>(() => store.Resolve("/tmp/outside.json"));
            Assert.False(store.IsWithinRoot(Path.Combine(root, "..", "outside.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RateLimit_AppliesOnlyToMutatingApiCalls()
    {
        var calls = 0;
        var middleware = new ApiRateLimitMiddleware(_ =>
        {
            calls++;
            return Task.CompletedTask;
        }, new SecurityOptions { RateLimitPerMinute = 2 });

        var post = new DefaultHttpContext();
        post.Request.Method = HttpMethods.Post;
        post.Request.Path = "/api/simulations";
        post.RequestServices = new ServiceCollection().BuildServiceProvider();
        await middleware.InvokeAsync(post);
        await middleware.InvokeAsync(post);
        await middleware.InvokeAsync(post);
        Assert.Equal(2, calls);
        Assert.Equal(StatusCodes.Status429TooManyRequests, post.Response.StatusCode);

        var get = new DefaultHttpContext();
        get.Request.Method = HttpMethods.Get;
        get.Request.Path = "/api/simulations";
        await middleware.InvokeAsync(get);
        Assert.Equal(3, calls);
    }
}
