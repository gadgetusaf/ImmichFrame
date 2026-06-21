using Microsoft.AspNetCore.Authentication;

public class CustomAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public CustomAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Admin and viewer endpoints use cookie auth, not the global bearer scheme.
        // Public slideshow links enforce their own slug-bound token inside SlideshowController,
        // so they must bypass the global AuthenticationSecret gate as well.
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/admin") || path.StartsWithSegments("/api/viewer")
            || path.StartsWithSegments("/api/slideshow") || path.StartsWithSegments("/slideshow"))
        {
            await _next(context);
            return;
        }

        var result = await context.AuthenticateAsync("ImmichFrameScheme");

        if (!result.Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(result.Failure?.Message ?? "Unauthorized");
            return;
        }

        await _next(context);
    }
}