namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Adds baseline security response headers (defense in depth; a fronting reverse proxy may add or
/// override these). The CSP is intentionally permissive enough not to break the SvelteKit SPA or
/// the slideshow (inline scripts/styles, blob media, external image hosts like weather icons).
/// Headers are applied via OnStarting so they survive a downstream Response.Clear().
/// </summary>
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "img-src 'self' data: blob: https:; " +
        "media-src 'self' blob:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "connect-src 'self'; " +
        "font-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'self'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            var headers = ctx.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;

            if (ctx.Request.IsHttps)
            {
                headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
            }

            return Task.CompletedTask;
        }, context);

        return next(context);
    }
}
