using System.Text;
using Fratak_Shop.Core.Services.Seo.Feeds;

namespace Fratak_Shop.Tests.Unit.Seo;

/// <summary>
/// تست‌های امنیتی API محصولات ترب: درستی تأیید امضای Ed25519 (بردارهای رسمی RFC 8032) و بررسی توکن JWT ترب.
/// <para>توکن‌های نمونه با کلید خصوصی بردار شمارهٔ ۱ RFC ساخته شده‌اند (نه کلید ترب)؛ برای همین کلید عمومی همان بردار به اعتبارسنج داده می‌شود.</para>
/// </summary>
public class TorobFeedSecurityTests
{
    // بردار ۱ RFC 8032 (پیام خالی)
    private const string T1Pub = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
    private const string T1Sig = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";
    private const string T1Spki = "MCowBQYDK2VwAyEA11qYAYKxCrfVS/7TyWQHOg7hcvPapiMlrwIaaPcHURo=";

    // بردار ۲ RFC 8032 (پیام یک‌بایتی 0x72)
    private const string T2Pub = "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c";
    private const string T2Sig = "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00";

    private const string Audience = "shop.fratak.ir";
    private const string H = "eyJhbGciOiJFZERTQSIsInR5cCI6IkpXVCIsInYiOjF9";
    private const string Valid = H + ".eyJhdWQiOiJzaG9wLmZyYXRhay5pciIsImV4cCI6NDEwMjQ0NDgwMCwibmJmIjoxNzAwMDAwMDAwfQ.FjT-9WyBCbiRffRrBanL3HgL9Ow-MxW_iLBmkyjOBgtgrhs9WdS6GEV5eNNHwHjSHQFGNl3tSj89NQGV5JrRCw";
    private const string Expired = H + ".eyJhdWQiOiJzaG9wLmZyYXRhay5pciIsImV4cCI6MTcwMDAwMDEwMCwibmJmIjoxNzAwMDAwMDAwfQ.EUT222zfMnU8nE7jFMfbAoQkyZUxbzHoRq7AKenMWY-6gV3UqNxghHy5xPISJnNysqYiHTXHDOfH_ze56lKRDQ";
    private const string WrongAud = H + ".eyJhdWQiOiJldmlsLmV4YW1wbGUiLCJleHAiOjQxMDI0NDQ4MDAsIm5iZiI6MTcwMDAwMDAwMH0.4AttbPCLYBu-H9xKLP8GZwbtswC1E_2EPfqlX5gpqtWQEZ1gAmqxc2p3YECAMxMwjezKrhtoNN6YvAkbBPm0DQ";
    private const string ArrayAud = H + ".eyJhdWQiOlsieC5leGFtcGxlIiwic2hvcC5mcmF0YWsuaXIiXSwiZXhwIjo0MTAyNDQ0ODAwLCJuYmYiOjE3MDAwMDAwMDB9.xbC4RfPUHMpTUaH791NZkrGRkr_D2vJKUxIv_5wQbDeDs53oM-z3N1c8w0auRU6OPiC5hyD4wqbz3ZnG1C4PAw";
    private const string Future = H + ".eyJhdWQiOiJzaG9wLmZyYXRhay5pciIsImV4cCI6NDEwMjQ0NDgwMCwibmJmIjo0MDAwMDAwMDAwfQ.AGIr8QcVAqyFzJjfP-Sj3SH7pMfur81vVZFbHv0PpcZSQ3VNepaWF03znuPSWiwJcqCe2O7GTrX34M3V2Vz0BA";
    private const string NoExp = H + ".eyJhdWQiOiJzaG9wLmZyYXRhay5pciIsIm5iZiI6MTcwMDAwMDAwMH0.gtID8LavNC-S0J4DULoJvSG_wYVgBO50JwgDPf8QoI0WymIEg_FRBEd9E0afbY0sfmwhC9dudzZ9oSHqP9LZCw";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static byte[] Key => Ed25519Verifier.PublicKeyFromSpki(T1Spki)!;

    [Fact]
    public void Ed25519_RfcVector1_Verifies()
        => Assert.True(Ed25519Verifier.Verify(Convert.FromHexString(T1Pub), [], Convert.FromHexString(T1Sig)));

    [Fact]
    public void Ed25519_RfcVector2_Verifies()
        => Assert.True(Ed25519Verifier.Verify(Convert.FromHexString(T2Pub), new byte[] { 0x72 }, Convert.FromHexString(T2Sig)));

    [Fact]
    public void Ed25519_ChangedMessage_IsRejected()
        => Assert.False(Ed25519Verifier.Verify(Convert.FromHexString(T1Pub), Encoding.ASCII.GetBytes("x"), Convert.FromHexString(T1Sig)));

    [Fact]
    public void Ed25519_SignatureOfAnotherKey_IsRejected()
        => Assert.False(Ed25519Verifier.Verify(Convert.FromHexString(T2Pub), [], Convert.FromHexString(T1Sig)));

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(63)]
    public void Ed25519_FlippedSignatureBit_IsRejected(int index)
    {
        var sig = Convert.FromHexString(T1Sig);
        sig[index] ^= 0x01;
        Assert.False(Ed25519Verifier.Verify(Convert.FromHexString(T1Pub), [], sig));
    }

    [Fact]
    public void Ed25519_WrongLengths_AreRejectedWithoutThrowing()
    {
        Assert.False(Ed25519Verifier.Verify(new byte[31], [], Convert.FromHexString(T1Sig)));
        Assert.False(Ed25519Verifier.Verify(Convert.FromHexString(T1Pub), [], new byte[63]));
        Assert.False(Ed25519Verifier.Verify([], [], []));
    }

    [Fact]
    public void Ed25519_NonCanonicalS_IsRejected()
    {
        // S ≥ L (ترتیب گروه) باید رد شود تا امضای «دستکاری‌شده با جمع L» پذیرفته نشود (حملهٔ malleability).
        var sig = Convert.FromHexString(T1Sig);
        for (var i = 32; i < 64; i++)
            sig[i] = 0xFF;
        Assert.False(Ed25519Verifier.Verify(Convert.FromHexString(T1Pub), [], sig));
    }

    [Fact]
    public void Spki_ParsesOfficialTorobKey_AndTestKey()
    {
        Assert.Equal(32, Ed25519Verifier.PublicKeyFromSpki(TorobTokenValidator.TorobPublicKey)!.Length);
        Assert.Equal(Convert.FromHexString(T1Pub), Key);
        var pem = "-----BEGIN PUBLIC KEY-----\n" + T1Spki + "\n-----END PUBLIC KEY-----";
        Assert.Equal(Key, Ed25519Verifier.PublicKeyFromSpki(pem));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void Spki_Garbage_ReturnsNull(string? value) => Assert.Null(Ed25519Verifier.PublicKeyFromSpki(value));

    [Fact]
    public void Token_Valid_IsAccepted() => Assert.Null(TorobTokenValidator.Validate(Valid, Audience, Now, Key));

    [Fact]
    public void Token_AudienceIsCaseInsensitive() => Assert.Null(TorobTokenValidator.Validate(Valid, "SHOP.Fratak.IR", Now, Key));

    [Fact]
    public void Token_ArrayAudienceContainingUs_IsAccepted() => Assert.Null(TorobTokenValidator.Validate(ArrayAud, Audience, Now, Key));

    [Fact]
    public void Token_Expired_IsRejected() => Assert.Equal("expired", TorobTokenValidator.Validate(Expired, Audience, Now, Key));

    [Fact]
    public void Token_ExpiredWithinClockSkew_IsAccepted()
        => Assert.Null(TorobTokenValidator.Validate(Expired, Audience, DateTimeOffset.FromUnixTimeSeconds(1_700_000_100 + 30), Key));

    [Fact]
    public void Token_WithoutExp_IsRejected() => Assert.Equal("expired", TorobTokenValidator.Validate(NoExp, Audience, Now, Key));

    [Fact]
    public void Token_NotYetValid_IsRejected() => Assert.Equal("not-yet-valid", TorobTokenValidator.Validate(Future, Audience, Now, Key));

    [Fact]
    public void Token_ForAnotherSite_IsRejected() => Assert.Equal("bad-audience", TorobTokenValidator.Validate(WrongAud, Audience, Now, Key));

    [Fact]
    public void Token_ValidForUs_IsRejectedOnAnotherHost() => Assert.Equal("bad-audience", TorobTokenValidator.Validate(Valid, "evil.example", Now, Key));

    [Fact]
    public void Token_TamperedSignature_IsRejected()
    {
        var last = Valid[^2] == 'A' ? 'B' : 'A';
        var tampered = Valid[..^2] + last + Valid[^1];
        Assert.Equal("bad-signature", TorobTokenValidator.Validate(tampered, Audience, Now, Key));
    }

    [Fact]
    public void Token_PayloadSwappedFromAnotherToken_IsRejected()
    {
        // حمله: payload توکن دیگری (با aud دیگر) را با امضای توکن معتبر ما ترکیب کنیم.
        var parts = Valid.Split('.');
        var evil = WrongAud.Split('.');
        Assert.Equal("bad-signature", TorobTokenValidator.Validate(parts[0] + "." + evil[1] + "." + parts[2], "evil.example", Now, Key));
    }

    [Fact]
    public void Token_SignedByTestKey_IsRejectedByRealTorobKey()
        => Assert.Equal("bad-signature", TorobTokenValidator.Validate(Valid, Audience, Now));

    [Fact]
    public void Token_AlgNone_IsRejected()
    {
        // حملهٔ کلاسیک alg=none: هدر {"alg":"none","typ":"JWT"}
        var none = Convert.ToBase64String(Encoding.ASCII.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var parts = Valid.Split('.');
        Assert.Equal("bad-alg", TorobTokenValidator.Validate(none + "." + parts[1] + ".", Audience, Now, Key));
        Assert.Equal("bad-alg", TorobTokenValidator.Validate(none + "." + parts[1] + "." + parts[2], Audience, Now, Key));
    }

    [Theory]
    [InlineData(null, "missing-token")]
    [InlineData("", "missing-token")]
    [InlineData("abc", "malformed")]
    [InlineData("a.b", "malformed")]
    [InlineData("a.b.c.d", "malformed")]
    [InlineData("!!!.???.***", "malformed")]
    public void Token_Malformed_IsRejected(string? token, string expected)
        => Assert.Equal(expected, TorobTokenValidator.Validate(token, Audience, Now, Key));

    [Fact]
    public void Token_Huge_IsRejectedEarly()
        => Assert.Equal("missing-token", TorobTokenValidator.Validate(new string('a', 10_000), Audience, Now, Key));

    [Fact]
    public void Token_WithoutConfiguredAudience_IsRejected()
        => Assert.Equal("audience-not-configured", TorobTokenValidator.Validate(Valid, "", Now, Key));

    [Theory]
    [InlineData("AQ", new byte[] { 1 })]
    [InlineData("AQI", new byte[] { 1, 2 })]
    [InlineData("AQID", new byte[] { 1, 2, 3 })]
    [InlineData("-_8", new byte[] { 0xFB, 0xFF })]
    public void Base64Url_Decodes(string text, byte[] expected) => Assert.Equal(expected, TorobTokenValidator.FromBase64Url(text));

    [Fact]
    public void Base64Url_BadLength_Throws() => Assert.Throws<FormatException>(() => TorobTokenValidator.FromBase64Url("A"));
}
