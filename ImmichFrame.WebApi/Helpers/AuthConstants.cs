namespace ImmichFrame.WebApi.Helpers;

public static class AuthConstants
{
    /// <summary>Cookie authentication scheme used for the configuration admin UI.</summary>
    public const string AdminCookieScheme = "AdminCookie";

    /// <summary>Authorization policy requiring an authenticated admin user.</summary>
    public const string AdminPolicy = "Admin";
}
