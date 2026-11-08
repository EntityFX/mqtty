using EntityFX.MqttY.Web.App.Security;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.Tests.Web;

[TestClass]
public class SecurityTests
{
    [TestMethod]
    public void JsonStore_RejectsPathTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-security-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonFileStore(root);
            Assert.ThrowsException<InvalidOperationException>(() => store.Resolve("../outside.json"));
            Assert.ThrowsException<InvalidOperationException>(() => store.Resolve("/tmp/outside.json"));
            Assert.IsFalse(store.IsWithinRoot(Path.Combine(root, "..", "outside.json")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
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
        Assert.AreEqual(2, calls);
        Assert.AreEqual(StatusCodes.Status429TooManyRequests, post.Response.StatusCode);

        var get = new DefaultHttpContext();
        get.Request.Method = HttpMethods.Get;
        get.Request.Path = "/api/simulations";
        await middleware.InvokeAsync(get);
        Assert.AreEqual(3, calls);
    }
}
