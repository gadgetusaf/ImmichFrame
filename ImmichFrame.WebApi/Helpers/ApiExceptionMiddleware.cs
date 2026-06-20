using ImmichFrame.Core.Api;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Converts unhandled Immich <see cref="ApiException"/>s (e.g. an asset request rejected for a
/// missing permission) into a clean 502 with a helpful message, instead of an unhandled 500 with a
/// full stack trace logged per asset. Endpoints that handle their own ApiException (like the
/// account browse) are unaffected — this is only a backstop for the rest.
/// </summary>
public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException ex)
        {
            var permission = ImmichErrors.MissingPermission(ex);
            logger.LogWarning("Immich returned {status} for {method} {path}{perm}.",
                ex.StatusCode, context.Request.Method, context.Request.Path,
                permission is null ? "" : $" (API key missing '{permission}')");

            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                context.Response.ContentType = "application/json";
                var message = permission is null
                    ? $"Immich returned an error (HTTP {ex.StatusCode})."
                    : $"Immich denied the request — the API key is missing the '{permission}' permission.";
                await context.Response.WriteAsJsonAsync(new { message });
            }
        }
    }
}
