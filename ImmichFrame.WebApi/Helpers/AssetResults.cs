using ImmichFrame.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>Shared asset streaming (range/partial aware) used by the global and per-link asset endpoints.</summary>
public static class AssetResults
{
    public static async Task<IActionResult> StreamAsync(ControllerBase controller, AssetResponse asset)
    {
        var response = controller.Response;
        response.Headers["Accept-Ranges"] = "bytes";
        // Private media: never let a CDN/shared proxy cache and cross-serve one viewer's photo/video to another.
        response.Headers["Cache-Control"] = "private, no-store";
        response.Headers["Vary"] = "Cookie";

        if (asset.IsPartial && !string.IsNullOrEmpty(asset.ContentRange))
        {
            response.Headers["Content-Range"] = asset.ContentRange;
            response.StatusCode = 206;
            response.ContentType = asset.ContentType;

            if (asset.FileStream is { CanSeek: true } && asset.FileStream.Length > 0)
                response.ContentLength = asset.FileStream.Length;
            else if (asset.ContentLength.HasValue)
                response.ContentLength = asset.ContentLength;

            using (asset.Owner)
            {
                await asset.FileStream.CopyToAsync(response.Body);
            }
            return new EmptyResult();
        }

        if (asset.Owner != null)
            controller.HttpContext.Response.RegisterForDispose(asset.Owner);

        return controller.File(asset.FileStream, asset.ContentType, asset.FileName, enableRangeProcessing: true);
    }
}
