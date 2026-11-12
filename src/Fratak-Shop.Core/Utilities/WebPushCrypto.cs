using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// Standard Web Push without third-party packages: message encryption (RFC 8291, "aes128gcm"
/// content coding, RFC 8188) and VAPID authentication (RFC 8292, ES256 JWT).
/// </summary>
public static class WebPushCrypto
{
    private const int RecordSize = 4096;

    public static (string PublicKey, string PrivateKey) GenerateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return (Base64Url.Encode(Uncompressed(parameters.Q)), Base64Url.Encode(parameters.D!));
    }

    /// <summary>True when the pair is a valid P-256 key pair (public key matches the private key).</summary>
    public static bool IsValidVapidPair(string? publicKey, string? privateKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
                return false;

            using var key = ImportSigningKey(publicKey, privateKey);
            var derived = Uncompressed(key.ExportParameters(includePrivateParameters: false).Q);
            return derived.AsSpan().SequenceEqual(Base64Url.Decode(publicKey));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Value of the Authorization header for a push service request.</summary>
    public static string CreateVapidAuthorization(string endpoint, string subject, string publicKey, string privateKey, DateTimeOffset expires)
    {
        var uri = new Uri(endpoint);
        var audience = uri.GetLeftPart(UriPartial.Authority);

        var header = Base64Url.Encode("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8.ToArray());
        var claims = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["sub"] = subject,
        }));

        var unsigned = header + "." + claims;
        using var key = ImportSigningKey(publicKey, privateKey);
        // .NET returns the IEEE P1363 (r || s) format JWS needs.
        var signature = key.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256);
        return $"vapid t={unsigned}.{Base64Url.Encode(signature)}, k={publicKey}";
    }

    /// <summary>Validates the keys a browser returned from <c>PushManager.subscribe()</c>.</summary>
    public static bool IsValidSubscriptionKeys(string? p256dh, string? auth)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
                return false;

            var publicKey = Base64Url.Decode(p256dh);
            var secret = Base64Url.Decode(auth);
            if (publicKey.Length != 65 || publicKey[0] != 0x04 || secret.Length != 16)
                return false;

            using var _ = ImportPublicKey(publicKey); // throws when the point is not on the curve
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Encrypts a payload for one subscription (single record, aes128gcm).</summary>
    public static byte[] Encrypt(byte[] payload, string p256dh, string auth, ECDiffieHellman? senderKey = null, byte[]? salt = null)
    {
        if (payload.Length > RecordSize - 103)
            throw new ArgumentException("Push payload is too large.", nameof(payload));

        var receiverPublic = Base64Url.Decode(p256dh);
        var authSecret = Base64Url.Decode(auth);
        salt ??= RandomNumberGenerator.GetBytes(16);

        using var ownedKey = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var localKey = senderKey ?? ownedKey!;
        var senderPublic = Uncompressed(localKey.ExportParameters(includePrivateParameters: false).Q);

        using var receiver = ImportPublicKey(receiverPublic);
        var sharedSecret = localKey.DeriveRawSecretAgreement(receiver.PublicKey);

        // RFC 8291 §3.4: IKM from the ECDH secret, bound to both public keys and the auth secret.
        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), receiverPublic, senderPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);
        var contentKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        var plaintext = new byte[payload.Length + 1];
        payload.CopyTo(plaintext, 0);
        plaintext[^1] = 0x02; // last-record delimiter, no padding

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(contentKey, 16))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Header: salt (16) | record size (4) | key id length (1) | key id = sender public key (65)
        var body = new byte[16 + 4 + 1 + senderPublic.Length + ciphertext.Length + tag.Length];
        var offset = 0;
        salt.CopyTo(body, offset); offset += 16;
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(offset, 4), RecordSize); offset += 4;
        body[offset++] = (byte)senderPublic.Length;
        senderPublic.CopyTo(body, offset); offset += senderPublic.Length;
        ciphertext.CopyTo(body, offset); offset += ciphertext.Length;
        tag.CopyTo(body, offset);
        return body;
    }

    /// <summary>Decrypts a message (used by the tests to prove the round trip).</summary>
    public static byte[] Decrypt(byte[] body, ECDiffieHellman receiverKey, byte[] authSecret)
    {
        var salt = body[..16];
        var idLength = body[20];
        var senderPublic = body[21..(21 + idLength)];
        var payload = body[(21 + idLength)..];

        var receiverPublic = Uncompressed(receiverKey.ExportParameters(false).Q);
        using var sender = ImportPublicKey(senderPublic);
        var sharedSecret = receiverKey.DeriveRawSecretAgreement(sender.PublicKey);

        var keyInfo = Concat("WebPush: info\0"u8.ToArray(), receiverPublic, senderPublic);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);
        var contentKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, "Content-Encoding: nonce\0"u8.ToArray());

        var ciphertext = payload[..^16];
        var tag = payload[^16..];
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(contentKey, 16))
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var end = Array.LastIndexOf(plaintext, (byte)0x02);
        return plaintext[..end];
    }

    public static byte[] Uncompressed(ECPoint point) => Concat([0x04], point.X!, point.Y!);

    private static ECDsa ImportSigningKey(string publicKey, string privateKey)
    {
        var q = Base64Url.Decode(publicKey);
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = q[1..33], Y = q[33..65] },
            D = Base64Url.Decode(privateKey),
        });
    }

    private static ECDiffieHellman ImportPublicKey(byte[] uncompressed) =>
        ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uncompressed[1..33], Y = uncompressed[33..65] },
        });

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}

/// <summary>URL-safe base64 without padding (JWT / Web Push keys).</summary>
public static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", 0 => s, _ => throw new FormatException("Invalid base64url.") };
        return Convert.FromBase64String(s);
    }
}
