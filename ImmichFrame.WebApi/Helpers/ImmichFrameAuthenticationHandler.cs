using ImmichFrame.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

public class ImmichFrameAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly string? _authenticationSecret;
    private readonly bool _requireSecret;

    public ImmichFrameAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServerSettings settings)
        : base(options, logger, encoder)
    {
        _authenticationSecret = settings.GeneralSettings.AuthenticationSecret;

        // Opt-in "links-only" mode: when no AuthenticationSecret is set, lock the global
        // content API instead of leaving it anonymously open. Only /slideshow/{slug} links
        // (which bypass this handler) stay reachable. Default (unset) preserves legacy behavior.
        var requireSecret = Environment.GetEnvironmentVariable("IMMICHFRAME_REQUIRE_SECRET");
        _requireSecret = requireSecret is not null &&
            (requireSecret.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             requireSecret.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             requireSecret.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var endpoint = Context.GetEndpoint();
        var authorizeAttribute = endpoint?.Metadata?.GetMetadata<IAuthorizeData>();

        if (authorizeAttribute == null)
        {
            // Endpoint isn't [Authorize]-protected (e.g. ConfigController); nothing to challenge.
            return Task.FromResult(AuthenticateResult.Success(AnonymousTicket()));
        }

        if (_authenticationSecret == null)
        {
            if (_requireSecret)
            {
                // Links-only mode: refuse the global content API when no secret is configured.
                return Task.FromResult(AuthenticateResult.Fail(
                    "This endpoint requires AuthenticationSecret (links-only mode); use a /slideshow/{slug} link instead."));
            }

            // Legacy default: no secret set => global content API is open.
            return Task.FromResult(AuthenticateResult.Success(AnonymousTicket()));
        }

        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));
        }

        var authHeader = Request.Headers["Authorization"].ToString();
        if (authHeader.StartsWith("Bearer ", System.StringComparison.OrdinalIgnoreCase))
        {
            var token = authHeader.Substring("Bearer ".Length).Trim();

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(token),
                    Encoding.UTF8.GetBytes(_authenticationSecret)))
            {
                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "authenticatedUser") };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("The AuthenticationSecret was not correct!"));
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header"));
    }

    private AuthenticationTicket AnonymousTicket()
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "anonymous") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}