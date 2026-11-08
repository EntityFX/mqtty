using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Security;

/// <summary>Simple fixed-window limiter for expensive mutating API endpoints.</summary>
public sealed class ApiRateLimitMiddleware
{
    private static readonly string[] LimitedPrefixes =
    {
        "/api/simulations",
        "/api/twin-validations",
        "/api/topologies",
        "/api/broker-profiles"
    };

    private readonly RequestDelegate _next;
    private readonly SecurityOptions _options;
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public ApiRateLimitMiddleware(RequestDelegate next, SecurityOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.RateLimitPerMinute <= 0 || !IsLimited(context))
        {
            await _next(context);
            return;
        }

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        var window = _windows.GetOrAdd(key, _ => new Window());
        if (!window.TryAcquire(_options.RateLimitPerMinute))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = $"Limit is {_options.RateLimitPerMinute} requests per minute."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsLimited(HttpContext context) =>
        !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
        LimitedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private sealed class Window
    {
        private readonly object _sync = new();
        private DateTime _start = DateTime.UtcNow;
        private int _count;

        public bool TryAcquire(int limit)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                if (now - _start >= TimeSpan.FromMinutes(1))
                {
                    _start = now;
                    _count = 0;
                }

                if (_count >= limit) return false;
                _count++;
                return true;
            }
        }
    }
}
