using System.Security.Cryptography;
using System.Text;

namespace Fratak_Shop.Data.Utilities;

/// <summary>
/// Creates stable, reproducible GUIDs from a text key.
/// Used by seeders so reference data keeps the same IDs across environments
/// and re-seeding never duplicates rows.
/// </summary>
public static class DeterministicGuid
{
    private static readonly byte[] NamespaceBytes = Encoding.UTF8.GetBytes("Fratak-Shop.Data/v1");

    /// <summary>
    /// Returns the same GUID for the same <paramref name="value"/> on every run.
    /// </summary>
    public static Guid Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var valueBytes = Encoding.UTF8.GetBytes(value);
        var data = new byte[NamespaceBytes.Length + valueBytes.Length];
        Buffer.BlockCopy(NamespaceBytes, 0, data, 0, NamespaceBytes.Length);
        Buffer.BlockCopy(valueBytes, 0, data, NamespaceBytes.Length, valueBytes.Length);

        var hash = SHA256.HashData(data);
        var guidBytes = hash.AsSpan(0, 16).ToArray();
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(guidBytes);
    }
}
