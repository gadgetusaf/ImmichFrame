using System.Text.RegularExpressions;
using ImmichFrame.Core.Api;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>Turns Immich API exceptions into human-readable, actionable messages.</summary>
public static partial class ImmichErrors
{
    [GeneratedRegex(@"Missing required permission:\s*([\w.]+)")]
    private static partial Regex PermissionRegex();

    /// <summary>Extracts e.g. "asset.view" from a 403 body like {"message":"Missing required permission: asset.view"}.</summary>
    public static string? MissingPermission(ApiException ex)
    {
        if (ex.StatusCode != 403 || string.IsNullOrEmpty(ex.Response)) return null;
        var match = PermissionRegex().Match(ex.Response);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string Describe(Exception e) => e switch
    {
        ApiException { StatusCode: 401 } => "the API key was rejected (401 Unauthorized).",
        ApiException api when MissingPermission(api) is { } p => $"the API key is missing the '{p}' permission.",
        ApiException { StatusCode: 403 } => "access was forbidden (403).",
        ApiException api => $"Immich returned HTTP {api.StatusCode}.",
        _ => e.Message
    };
}
