using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EntityFX.MqttY.Web.App.Security;

/// <summary>Optional API-key authentication. Empty key keeps the local app open.</summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    private readonly SecurityOptions _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IOptions<SecurityOptions> security)
        : base(options, logger, encoder, clock)
    {
        _security = security.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (string.IsNullOrWhiteSpace(_security.ApiKey))
        {
            return Task.FromResult(AuthenticateResult.Success(CreateTicket("anonymous-local")));
        }

        var supplied = Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied))
        {
            var authorization = Request.Headers.Authorization.FirstOrDefault();
            if (authorization != null &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                supplied = authorization["Bearer ".Length..].Trim();
            }
        }

        return Task.FromResult(Matches(supplied, _security.ApiKey)
            ? AuthenticateResult.Success(CreateTicket("api-key"))
            : AuthenticateResult.Fail("A valid API key is required."));
    }

    private AuthenticationTicket CreateTicket(string name)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name) }, SchemeName);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
    }

    private static bool Matches(string? supplied, string expected)
    {
        if (supplied == null) return false;
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
