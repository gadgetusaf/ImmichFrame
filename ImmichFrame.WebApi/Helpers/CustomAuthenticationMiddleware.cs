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
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/admin") || path.StartsWithSegments("/api/viewer"))
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