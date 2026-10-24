using System.Security.Cryptography;
using Fratak_Shop.Core.Services.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Fratak_Shop.Web.Infrastructure.Engagement;

/// <summary><see cref="ISecretProtector"/> on top of ASP.NET Core Data Protection (keys persisted with the app's key ring).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Fratak.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string protectedValue)
    {
        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
