using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fratak_Shop.Core.Services.Seo.Tools;

// ═══════════════════════════════════════════════════════════════════════════
//  اتصال رایگان به Google Search Console (مرحلهٔ هفتم – فاز ۸ب) — بخش «بدون دیتابیس» و قابل تست
//  روش: «حساب سرویس» (Service Account) گوگل. رایگان است و نیاز به ورود دستی/OAuth ندارد:
//   ۱) در console.cloud.google.com یک پروژه بسازید و «Google Search Console API» را Enable کنید.
//   ۲) IAM → Service Accounts → Create → Keys → Add key → JSON؛ فایل JSON را در پنل بارگذاری کنید.
//   ۳) در Search Console → تنظیمات → کاربران و مجوزها، ایمیل حساب سرویس (…@….iam.gserviceaccount.com) را اضافه کنید.
//  جریان: JWT امضاشده با کلید خصوصی (RS256) → توکن دسترسی یک‌ساعته از oauth2.googleapis.com → فراخوانی API.
//  هیچ کتابخانهٔ گوگلی لازم نیست؛ همه با System.Security.Cryptography و System.Text.Json.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// اطلاعات لازم از فایل JSON حساب سرویس گوگل.
/// <example>{"type":"service_account","client_email":"seo@proj.iam.gserviceaccount.com","private_key":"-----BEGIN PRIVATE KEY-----…","private_key_id":"ab12…","token_uri":"https://oauth2.googleapis.com/token"}</example>
/// </summary>
public sealed record GscServiceAccount(string ClientEmail, string PrivateKeyPem, string? PrivateKeyId, string TokenUri, string? ProjectId)
{
    public const string DefaultTokenUri = "https://oauth2.googleapis.com/token";

    /// <summary>
    /// خواندن و اعتبارسنجی فایل JSON (کلید خصوصی واقعاً باید قابل بارگذاری باشد). خطا به فارسی در error.
    /// محل استفاده: ذخیرهٔ تنظیمات (برای پیام خطای فوری) و پیش از هر اتصال.
    /// </summary>
    public static bool TryParse(string? json, out GscServiceAccount? account, out string error)
    {
        account = null;
        error = "";
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "فایل کلید خالی است.";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "فایل کلید JSON معتبری نیست.";
                return false;
            }
            string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            if (Read("type") is { } type && type != "service_account")
            {
                error = "این فایل کلید «حساب سرویس» نیست (نوع: " + type + "). در Google Cloud از بخش Service Accounts کلید JSON بسازید.";
                return false;
            }
            var email = Read("client_email")?.Trim();
            var key = Read("private_key");
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            {
                error = "ایمیل حساب سرویس (client_email) در فایل نیست.";
                return false;
            }
            if (string.IsNullOrEmpty(key) || !key.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                error = "کلید خصوصی (private_key) در فایل نیست.";
                return false;
            }
            using (var rsa = RSA.Create())
                rsa.ImportFromPem(key);

            var tokenUri = Read("token_uri");
            if (!Uri.TryCreate(tokenUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.EndsWith("googleapis.com", StringComparison.OrdinalIgnoreCase))
                tokenUri = DefaultTokenUri; // فقط آدرس رسمی گوگل؛ فایل دست‌کاری‌شده نتواند کلید را جای دیگری بفرستد

            account = new GscServiceAccount(email, key, Read("private_key_id"), tokenUri!, Read("project_id"));
            return true;
        }
        catch (JsonException)
        {
            error = "فایل کلید JSON معتبری نیست.";
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            error = "کلید خصوصی داخل فایل خوانده نشد (فایل ناقص یا دست‌کاری شده است).";
        }
        return false;
    }
}

/// <summary>ساخت JWT امضاشده (RS256) برای گرفتن توکن دسترسی گوگل.</summary>
public static class GscJwt
{
    /// <summary>فقط خواندن آمار.</summary>
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/webmasters.readonly";

    /// <summary>خواندن + ثبت نقشهٔ سایت.</summary>
    public const string FullScope = "https://www.googleapis.com/auth/webmasters";

    /// <summary>
    /// JWT با اعتبار یک ساعت. aud = token_uri، iss = ایمیل حساب سرویس.
    /// <example>header {"alg":"RS256","typ":"JWT","kid":"ab12"} · claims {"iss":"…","scope":"…","aud":"https://oauth2.googleapis.com/token","iat":…,"exp":iat+3600}</example>
    /// </summary>
    public static string Create(GscServiceAccount account, string scope, DateTimeOffset now)
    {
        var header = new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" };
        if (!string.IsNullOrEmpty(account.PrivateKeyId))
            header["kid"] = account.PrivateKeyId;
        var issuedAt = now.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object>
        {
            ["iss"] = account.ClientEmail,
            ["scope"] = scope,
            ["aud"] = account.TokenUri,
            ["iat"] = issuedAt,
            ["exp"] = issuedAt + 3600,
        };
        var unsigned = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header)) + "." + Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(account.PrivateKeyPem);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return unsigned + "." + Base64Url(signature);
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>یک ردیف خروجی searchAnalytics/query. Keys به ترتیب dimensions درخواست (مثلاً [page, query]).</summary>
public sealed record GscRow(IReadOnlyList<string> Keys, double Clicks, double Impressions, double Ctr, double Position);

/// <summary>خواندن پاسخ‌های JSON گوگل (هرگز استثنا نمی‌دهد؛ پاسخ خراب = خروجی خالی).</summary>
public static class GscParsers
{
    /// <summary>پاسخ توکن: {"access_token":"ya29…","expires_in":3599,"token_type":"Bearer"}.</summary>
    public static (string? Token, int ExpiresIn) Token(string? body)
    {
        var root = Parse(body);
        if (root is not { } r || !r.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String)
            return (null, 0);
        var expires = r.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        return (token.GetString(), expires);
    }

    /// <summary>فهرست ویژگی‌ها: {"siteEntry":[{"siteUrl":"sc-domain:fratak.ir","permissionLevel":"siteOwner"}]}.</summary>
    public static List<(string SiteUrl, string Permission)> Sites(string? body)
    {
        var list = new List<(string, string)>();
        if (Parse(body) is { } root && root.TryGetProperty("siteEntry", out var entries) && entries.ValueKind == JsonValueKind.Array)
            foreach (var entry in entries.EnumerateArray())
                if (entry.TryGetProperty("siteUrl", out var url) && url.ValueKind == JsonValueKind.String)
                    list.Add((url.GetString()!, entry.TryGetProperty("permissionLevel", out var level) ? level.GetString() ?? "" : ""));
        return list;
    }

    /// <summary>ردیف‌های آمار: {"rows":[{"keys":["https://…","قیمت a55"],"clicks":3,"impressions":120,"ctr":0.025,"position":7.4}]}.</summary>
    public static List<GscRow> Rows(string? body)
    {
        var list = new List<GscRow>();
        if (Parse(body) is not { } root || !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array)
                continue;
            var values = keys.EnumerateArray().Select(k => k.ValueKind == JsonValueKind.String ? k.GetString() ?? "" : k.ToString()).ToList();
            list.Add(new GscRow(values, Number(row, "clicks"), Number(row, "impressions"), Number(row, "ctr"), Number(row, "position")));
        }
        return list;
    }

    /// <summary>
    /// پیام خطای گوگل. دو قالب: API ← {"error":{"code":403,"message":"…","status":"PERMISSION_DENIED"}}،
    /// توکن ← {"error":"invalid_grant","error_description":"…"}.
    /// </summary>
    public static string? Error(string? body)
    {
        if (Parse(body) is not { } root || !root.TryGetProperty("error", out var error))
            return null;
        if (error.ValueKind == JsonValueKind.String)
        {
            var code = error.GetString();
            return root.TryGetProperty("error_description", out var description) && description.ValueKind == JsonValueKind.String
                ? code + ": " + description.GetString()
                : code;
        }
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            return message.GetString();
        return null;
    }

    private static double Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : 0;

    private static JsonElement? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>ترجمهٔ خطاهای گوگل به راهنمای فارسی قابل اقدام (برای کارشناس سئو، نه برنامه‌نویس).</summary>
public static class GscErrors
{
    /// <summary>
    /// status = کد HTTP (null یعنی اتصال برقرار نشد)، body = پاسخ گوگل، transport = خطای شبکه، email = ایمیل حساب سرویس.
    /// <example>403 → «ایمیل … را در Search Console → تنظیمات → کاربران و مجوزها اضافه کنید».</example>
    /// </summary>
    public static string Explain(int? status, string? body, string? transport, string? email)
    {
        var google = GscParsers.Error(body);
        var detail = string.IsNullOrWhiteSpace(google) ? "" : " (پیام گوگل: " + Cut(google, 160) + ")";
        var lower = (google ?? "").ToLowerInvariant();
        switch (status)
        {
            case null:
                return "اتصال به گوگل برقرار نشد" + (string.IsNullOrWhiteSpace(transport) ? "" : " (" + Cut(transport, 120) + ")") +
                       ". سرورهای داخل ایران معمولاً به APIهای گوگل دسترسی ندارند؛ در همین صفحه آدرس یک پروکسی وارد کنید " +
                       "(مثلاً http://127.0.0.1:10809 یا socks5://127.0.0.1:1080).";
            case 400 when lower.Contains("invalid_grant"):
                return "گوگل کلید را نپذیرفت: یا ساعت سرور بیش از چند دقیقه اختلاف دارد، یا این کلید در Google Cloud حذف شده است. " +
                       "ساعت سرور را تنظیم کنید یا یک کلید JSON تازه بسازید." + detail;
            case 400:
                return "درخواست نامعتبر بود؛ آدرس ویژگی را دقیقاً مثل Search Console وارد کنید." + detail;
            case 401:
                return "دسترسی پذیرفته نشد؛ کلید JSON را دوباره بسازید و بارگذاری کنید." + detail;
            case 403 when lower.Contains("has not been used") || lower.Contains("disabled") || lower.Contains("not enabled"):
                return "«Google Search Console API» در پروژهٔ Google Cloud فعال نیست: console.cloud.google.com ← APIs & Services ← Library ← " +
                       "Google Search Console API ← Enable. چند دقیقه بعد دوباره امتحان کنید." + detail;
            case 403 when lower.Contains("country") || lower.Contains("region") || lower.Contains("location"):
                return "گوگل درخواست را از این منطقه نمی‌پذیرد؛ یک پروکسی خارج از ایران تنظیم کنید." + detail;
            case 403:
                return "حساب سرویس به این ویژگی دسترسی ندارد. در Search Console ← تنظیمات ← کاربران و مجوزها ← افزودن کاربر، ایمیل " +
                       (email ?? "حساب سرویس") + " را با دسترسی «کامل» اضافه کنید (برای ثبت نقشهٔ سایت دسترسی «مالک» لازم است)." + detail;
            case 404:
                return "این ویژگی در Search Console پیدا نشد. آدرس را دقیقاً مثل خود Search Console بنویسید: برای ویژگی دامنه «sc-domain:fratak.ir» " +
                       "و برای ویژگی آدرس «https://fratak.ir/» (با / پایانی)." + detail;
            case 429:
                return "سقف درخواست روزانهٔ گوگل پر شده؛ چند ساعت بعد دوباره امتحان کنید." + detail;
            case >= 500:
                return "خطای موقت سرور گوگل؛ کمی بعد دوباره امتحان کنید." + detail;
            default:
                return "پاسخ غیرمنتظره از گوگل (کد " + status.Value.ToString(CultureInfo.InvariantCulture) + ")." + detail;
        }
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

/// <summary>قالب آدرس ویژگی‌های Search Console.</summary>
public static class SearchConsoleSites
{
    /// <summary>
    /// یکسان‌سازی ورودی کاربر. مثال: «fratak.ir» → «sc-domain:fratak.ir»، «https://fratak.ir» → «https://fratak.ir/»،
    /// «sc-domain:Fratak.ir» → «sc-domain:fratak.ir». ورودی نامعتبر → null.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
        {
            var domain = value[10..].Trim().TrimEnd('/').ToLowerInvariant();
            return IsDomain(domain) ? "sc-domain:" + domain : null;
        }
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsDomain(uri.Host))
                return null;
            var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";
            return uri.Scheme + "://" + uri.Authority.ToLowerInvariant() + path;
        }
        var bare = value.TrimEnd('/').ToLowerInvariant();
        return IsDomain(bare) ? "sc-domain:" + bare : null;
    }

    /// <summary>دامنهٔ بدون www ویژگی. مثال: «sc-domain:fratak.ir» و «https://www.fratak.ir/» → «fratak.ir».</summary>
    public static string? Host(string? site)
    {
        if (string.IsNullOrWhiteSpace(site))
            return null;
        if (site.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
            return SeoPaths.BareHost(site[10..]);
        return Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? SeoPaths.BareHost(uri.Host) : null;
    }

    /// <summary>آدرس نقشهٔ سایت برای ثبت در گوگل. مثال: «sc-domain:fratak.ir» → «https://fratak.ir/sitemap.xml».</summary>
    public static string? SitemapUrl(string? site)
    {
        if (string.IsNullOrWhiteSpace(site))
            return null;
        if (site.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
            return "https://" + site[10..] + "/sitemap.xml";
        return Uri.TryCreate(site, UriKind.Absolute, out var uri) ? uri.Scheme + "://" + uri.Authority + "/sitemap.xml" : null;
    }

    /// <summary>پروکسی معتبر: http/https/socks4/socks5 با دامنه و پورت. مثال: «socks5://127.0.0.1:1080».</summary>
    public static bool IsValidProxy(string? proxy) =>
        Uri.TryCreate(proxy?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "socks4" or "socks4a" or "socks5" && !string.IsNullOrEmpty(uri.Host);

    private static bool IsDomain(string value) =>
        value.Length is > 2 and <= 253 && value.Contains('.') && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '-');
}

/// <summary>
/// برآورد نرخ کلیک بر اساس جایگاه (میانگین مطالعه‌های عمومی CTR گوگل). برای پیدا کردن «نرخ کلیک کم» و برآورد کلیک‌های از دست‌رفته.
/// <example>Expected(1) = 0.28، Expected(5) = 0.05، Expected(15) = 0.01.</example>
/// </summary>
public static class GscCtr
{
    private static readonly double[] ByPosition = [0.28, 0.15, 0.10, 0.07, 0.05, 0.04, 0.03, 0.025, 0.02, 0.018];

    public static double Expected(double position)
    {
        var rank = (int)Math.Round(position, MidpointRounding.AwayFromZero);
        return rank < 1 ? ByPosition[0] : rank <= ByPosition.Length ? ByPosition[rank - 1] : 0.01;
    }
}
