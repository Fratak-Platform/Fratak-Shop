using System.Text;
using System.Text.Json;

namespace Fratak_Shop.Core.Services.Seo.Feeds;

/// <summary>
/// بررسی توکن درخواست‌های ترب به API محصولات (راهنمای رسمی Torob-Sync، «token guide»).
/// <para>قانون‌ها: هدر X-Torob-Token یک JWT با الگوریتم EdDSA است که با کلید خصوصی ترب امضا شده؛ فیلد aud باید دقیقاً نام دامنهٔ
/// API ما باشد (از تنظیمات، نه از هدر Host که جعل‌شدنی است)؛ exp و nbf رعایت شوند. هر خطا ← ۴۰۱.</para>
/// <para>کجا استفاده می‌شود؟ نقطهٔ پایانی <c>POST /torob_api/v3/products</c> در لایهٔ وب.</para>
/// </summary>
public static class TorobTokenValidator
{
    /// <summary>کلید عمومی رسمی ترب (SubjectPublicKeyInfo، Base64).</summary>
    public const string TorobPublicKey = "MCowBQYDK2VwAyEAt6Mu4T0pBORY11W+QeM35UsmLO3vsf+6yKpFDEImFk0=";

    /// <summary>اختلاف ساعت مجاز (ثانیه) بین سرور ما و ترب.</summary>
    public const int ClockSkewSeconds = 60;

    /// <summary>
    /// نتیجهٔ بررسی: null = معتبر؛ در غیر این صورت دلیل (برای لاگ؛ به ترب فقط ۴۰۱ برمی‌گردد).
    /// <example><code>
    /// var error = TorobTokenValidator.Validate(token, "shop.fratak.ir", DateTimeOffset.UtcNow);
    /// if (error is not null) return Results.Unauthorized();
    /// </code></example>
    /// </summary>
    /// <param name="token">مقدار هدر X-Torob-Token.</param>
    /// <param name="audience">دامنهٔ API (مثال: «shop.fratak.ir»؛ اگر پورت غیراستاندارد دارد، همراه پورت).</param>
    /// <param name="now">زمان فعلی.</param>
    /// <param name="publicKey">کلید عمومی (پیش‌فرض: کلید ترب؛ تست‌ها کلید خودشان را می‌دهند).</param>
    public static string? Validate(string? token, string? audience, DateTimeOffset now, byte[]? publicKey = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            return "missing-token";
        if (string.IsNullOrWhiteSpace(audience))
            return "audience-not-configured";
        var parts = token.Trim().Split('.');
        if (parts.Length != 3)
            return "malformed";

        var key = publicKey ?? Ed25519Verifier.PublicKeyFromSpki(TorobPublicKey);
        if (key is null)
            return "bad-key";

        byte[] header, payload, signature;
        try
        {
            header = FromBase64Url(parts[0]);
            payload = FromBase64Url(parts[1]);
            signature = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            return "malformed";
        }

        try
        {
            using (var h = JsonDocument.Parse(header))
            {
                if (h.RootElement.ValueKind != JsonValueKind.Object
                    || !h.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String
                    || alg.GetString() != "EdDSA")
                    return "bad-alg";
            }

            if (!Ed25519Verifier.Verify(key, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature))
                return "bad-signature";

            using var p = JsonDocument.Parse(payload);
            var root = p.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return "malformed";
            var seconds = now.ToUnixTimeSeconds();
            if (!root.TryGetProperty("exp", out var exp) || exp.ValueKind != JsonValueKind.Number || exp.GetDouble() + ClockSkewSeconds < seconds)
                return "expired";
            if (root.TryGetProperty("nbf", out var nbf) && (nbf.ValueKind != JsonValueKind.Number || nbf.GetDouble() - ClockSkewSeconds > seconds))
                return "not-yet-valid";
            if (!root.TryGetProperty("aud", out var aud) || !AudienceMatches(aud, audience))
                return "bad-audience";
        }
        catch (JsonException)
        {
            return "malformed";
        }

        return null;
    }

    private static bool AudienceMatches(JsonElement aud, string audience) => aud.ValueKind switch
    {
        JsonValueKind.String => string.Equals(aud.GetString(), audience, StringComparison.OrdinalIgnoreCase),
        JsonValueKind.Array => aud.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && string.Equals(a.GetString(), audience, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    /// <summary>رمزگشایی Base64Url (بدون =). نامعتبر ← FormatException.</summary>
    public static byte[] FromBase64Url(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text = (text.Length % 4) switch
        {
            2 => text + "==",
            3 => text + "=",
            1 => throw new FormatException("bad base64url length"),
            _ => text,
        };
        return Convert.FromBase64String(text);
    }
}
