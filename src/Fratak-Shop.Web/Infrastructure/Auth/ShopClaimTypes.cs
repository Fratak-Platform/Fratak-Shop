namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>Compact claim names used in the access token.</summary>
public static class ShopClaimTypes
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Phone = "phone_number";
    public const string Role = "role";

    /// <summary>Security stamp of the user when the token was issued.</summary>
    public const string SecurityStamp = "sst";

    /// <summary>Refresh-token family (= login session / device).</summary>
    public const string SessionId = "sid";

    /// <summary>"1" when the session was created with "remember me".</summary>
    public const string Persistent = "rme";
}
