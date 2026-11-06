using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using B64 = Fratak_Shop.Core.Utilities.Base64Url;

namespace Fratak_Shop.Tests.Unit;

public class WebPushCryptoTests
{
    private static (ECDiffieHellman Key, string P256dh, byte[] Auth) NewSubscription()
    {
        var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p256dh = B64.Encode(WebPushCrypto.Uncompressed(key.ExportParameters(false).Q));
        return (key, p256dh, RandomNumberGenerator.GetBytes(16));
    }

    [Fact]
    public void GeneratedVapidKeys_AreAValidPair()
    {
        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();

        Assert.True(WebPushCrypto.IsValidVapidPair(publicKey, privateKey));
        var raw = B64.Decode(publicKey);
        Assert.Equal(65, raw.Length);
        Assert.Equal(0x04, raw[0]);
        Assert.Equal(32, B64.Decode(privateKey).Length);
    }

    [Fact]
    public void IsValidVapidPair_RejectsMismatchedOrGarbageKeys()
    {
        var a = WebPushCrypto.GenerateVapidKeys();
        var b = WebPushCrypto.GenerateVapidKeys();

        Assert.False(WebPushCrypto.IsValidVapidPair(a.PublicKey, b.PrivateKey));
        Assert.False(WebPushCrypto.IsValidVapidPair("not-a-key", a.PrivateKey));
        Assert.False(WebPushCrypto.IsValidVapidPair(null, null));
        Assert.False(WebPushCrypto.IsValidVapidPair(a.PublicKey, ""));
    }

    [Fact]
    public void VapidAuthorization_IsASignedEs256Jwt()
    {
        var (publicKey, privateKey) = WebPushCrypto.GenerateVapidKeys();
        var expires = DateTimeOffset.UtcNow.AddHours(12);

        var header = WebPushCrypto.CreateVapidAuthorization("https://fcm.googleapis.com/fcm/send/abc", "mailto:shop@example.com", publicKey, privateKey, expires);

        Assert.StartsWith("vapid t=", header);
        Assert.EndsWith(", k=" + publicKey, header);
        var jwt = header["vapid t=".Length..header.IndexOf(", k=", StringComparison.Ordinal)];
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        using var claims = JsonDocument.Parse(B64.Decode(parts[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal("mailto:shop@example.com", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal(expires.ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());

        var point = B64.Decode(publicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
        });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), B64.Decode(parts[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Encrypt_RoundTripsThroughDecrypt()
    {
        var (key, p256dh, auth) = NewSubscription();
        using var _ = key;
        var payload = Encoding.UTF8.GetBytes("{\"title\":\"سفارش شما ارسال شد\",\"url\":\"/profile/orders/1001\"}");

        var body = WebPushCrypto.Encrypt(payload, p256dh, B64.Encode(auth));

        Assert.NotEqual(payload, body);
        Assert.Equal(payload, WebPushCrypto.Decrypt(body, key, auth));
    }

    [Fact]
    public void Encrypt_UsesAFreshKeyAndSaltEachTime()
    {
        var (key, p256dh, auth) = NewSubscription();
        using var _ = key;
        var payload = "hello"u8.ToArray();

        Assert.NotEqual(WebPushCrypto.Encrypt(payload, p256dh, B64.Encode(auth)), WebPushCrypto.Encrypt(payload, p256dh, B64.Encode(auth)));
    }

    [Fact]
    public void Decrypt_FailsWithAnotherAuthSecret()
    {
        var (key, p256dh, auth) = NewSubscription();
        using var _ = key;
        var body = WebPushCrypto.Encrypt("secret"u8.ToArray(), p256dh, B64.Encode(auth));

        Assert.ThrowsAny<CryptographicException>(() => WebPushCrypto.Decrypt(body, key, RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public void Encrypt_RejectsOversizedPayload()
    {
        var (key, p256dh, auth) = NewSubscription();
        using var _ = key;
        Assert.Throws<ArgumentException>(() => WebPushCrypto.Encrypt(new byte[4096], p256dh, B64.Encode(auth)));
    }

    [Fact]
    public void IsValidSubscriptionKeys_ChecksShapeAndCurve()
    {
        var (key, p256dh, auth) = NewSubscription();
        using var _ = key;

        Assert.True(WebPushCrypto.IsValidSubscriptionKeys(p256dh, B64.Encode(auth)));
        Assert.False(WebPushCrypto.IsValidSubscriptionKeys(p256dh, B64.Encode(new byte[8])));
        Assert.False(WebPushCrypto.IsValidSubscriptionKeys(B64.Encode(new byte[65]), B64.Encode(auth)));
        Assert.False(WebPushCrypto.IsValidSubscriptionKeys("%%%", B64.Encode(auth)));
        Assert.False(WebPushCrypto.IsValidSubscriptionKeys(null, null));
    }

    [Fact]
    public void Base64Url_RoundTripsUrlUnsafeBytes()
    {
        var data = new byte[] { 0xfb, 0xff, 0xfe, 0x3e, 0x3f, 0x00 };
        var encoded = B64.Encode(data);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Equal(data, B64.Decode(encoded));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://android.googleapis.com/gcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://web.push.apple.com/QGx", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://evil.example.com/fcm.googleapis.com", false)]
    [InlineData("https://fcm.googleapis.com.evil.example.com/x", false)]
    [InlineData("https://user:pass@fcm.googleapis.com/x", false)]
    [InlineData("https://fcm.googleapis.com:8443/x", false)]
    [InlineData("https://localhost/push", false)]
    [InlineData("https://127.0.0.1/push", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsAllowedEndpoint_OnlyAcceptsKnownPushServices(string? endpoint, bool expected) =>
        Assert.Equal(expected, IWebPushService.IsAllowedEndpoint(endpoint));
}
