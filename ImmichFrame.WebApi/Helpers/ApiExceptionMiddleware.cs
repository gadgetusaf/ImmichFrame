using ImmichFrame.Core.Api;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Converts unhandled Immich <see cref="ApiException"/>s (e.g. an asset request rejected for a
/// missing permission) into a clean 502 with a helpful message, instead of an unhandled 500 with a
/// full stack trace logged per asset. Endpoints that handle their own ApiException (like the
/// account browse) are unaffected — this is only a backstop for the rest.
///
/// Any other unhandled exception (the ImmichFrameException family, or an unexpected error) is turned
/// into a generic JSON 500 so the client never sees a stack trace; the full detail is logged
/// server-side. Acts as the production catch-all exception handler for the API.
/// </summary>
public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IHostEnvironment env)
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

            var message = permission is null
                ? $"Immich returned an error (HTTP {ex.StatusCode})."
                : $"Immich denied the request — the API key is missing the '{permission}' permission.";
            await WriteError(context, StatusCodes.Status502BadGateway, message);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected mid-request (common for streamed media). Not a server error —
            // let it unwind quietly without logging an error or trying to write a body.
            throw;
        }
        catch (Exception ex)
        {
            // Log the full exception server-side; never leak the message/stack to the client.
            logger.LogError(ex, "Unhandled exception for {method} {path}.",
                context.Request.Method, context.Request.Path);

            // In Development, rethrow so the developer exception page (registered just outside this
            // middleware) can render full diagnostics. In Production, return a generic JSON 500.
            if (env.IsDevelopment())
                throw;

            await WriteError(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    // Clear() resets status/body but preserves headers already set by upstream middleware
    // (e.g. SecurityHeadersMiddleware), so the error response keeps its security headers.
    // If the response has already started we can't rewrite it — let it stand rather than throw.
    private static async Task WriteError(HttpContext context, int statusCode, string message)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { message });
    }
}
