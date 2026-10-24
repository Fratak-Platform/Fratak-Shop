namespace Fratak_Shop.Core.Services.Security;

/// <summary>
/// Reversible encryption of small secrets stored in the database (e.g. gift card codes that the
/// buyer can view again). Implemented by the web layer with ASP.NET Core Data Protection.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    /// <summary>Null when the value can't be decrypted (keys rotated / tampered).</summary>
    string? Unprotect(string protectedValue);
}
