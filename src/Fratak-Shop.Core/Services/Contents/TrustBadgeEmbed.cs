using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Fratak_Shop.Core.Services.Contents;

/// <summary>نتیجهٔ خواندن کد نماد: نشانی تصویر و لینک (یا پیام خطا).</summary>
public sealed record TrustBadgeEmbedResult(string? ImageUrl, string? LinkUrl, string? Error)
{
    public bool IsValid => Error is null && ImageUrl is not null;
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۶): پشتیبانی از «چسباندن کد نماد» (اینماد، ساماندهی، اتحادیه، درگاه پرداخت …) به‌جای بارگذاری تصویر.
/// <para>
/// کد چسبانده‌شده <b>هرگز خام نمایش داده نمی‌شود</b> (خطر اسکریپت). فقط دو چیز از آن برداشته می‌شود:
/// نشانی تصویر (<c>&lt;img src&gt;</c>) و لینک (<c>&lt;a href&gt;</c> یا نشانی داخل <c>onclick="window.open('…')"</c> که ساماندهی استفاده می‌کند).
/// هر دو باید https و دامنه‌دار باشند (نه IP، نه javascript:). پانویس سایت با قالب امن خودش و <c>referrerpolicy="origin"</c>
/// (که اینماد برای تأیید دامنه لازم دارد) نماد را می‌سازد.
/// </para>
/// <para>
/// مثال: <c>&lt;a referrerpolicy='origin' target='_blank' href='https://trustseal.enamad.ir/?id=123&amp;Code=abc'&gt;&lt;img referrerpolicy='origin'
/// src='https://trustseal.enamad.ir/logo.aspx?id=123&amp;Code=abc' alt='' code='abc'&gt;&lt;/a&gt;</c>
/// ← تصویر <c>https://trustseal.enamad.ir/logo.aspx?id=123&amp;Code=abc</c> و لینک <c>https://trustseal.enamad.ir/?id=123&amp;Code=abc</c>.
/// </para>
/// <para>محل استفاده: جدول «نمادهای اعتماد» پنل (ManageRegistry، فیلد «کد نماد»).</para>
/// </summary>
public static partial class TrustBadgeEmbed
{
    /// <summary>بیشترین طول کد چسبانده‌شده.</summary>
    public const int MaxLength = 2000;

    [GeneratedRegex(@"window\.open\(\s*['""](?<url>[^'""]+)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex WindowOpen();

    /// <summary>خواندن کد نماد. ورودی خالی ← (null, null, null) یعنی «کدی داده نشده».</summary>
    public static TrustBadgeEmbedResult Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new TrustBadgeEmbedResult(null, null, null);
        if (html.Length > MaxLength)
            return new TrustBadgeEmbedResult(null, null, $"کد نماد حداکثر {MaxLength} نویسه است.");

        var document = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        var image = document.QuerySelector("img[src]");
        var imageUrl = SafeUrl(image?.GetAttribute("src"));
        if (imageUrl is null)
            return new TrustBadgeEmbedResult(null, null, "در کد نماد تصویری با نشانی https پیدا نشد.");

        string? link = null;
        var anchor = image!.Closest("a[href]") ?? document.QuerySelector("a[href]");
        if (anchor is not null)
            link = SafeUrl(anchor.GetAttribute("href"));
        if (link is null)
        {
            var onclick = image.GetAttribute("onclick") ?? anchor?.GetAttribute("onclick");
            if (onclick is not null && WindowOpen().Match(onclick) is { Success: true } match)
                link = SafeUrl(WebUtility.HtmlDecode(match.Groups["url"].Value));
        }
        return new TrustBadgeEmbedResult(imageUrl, link, null);
    }

    /// <summary>
    /// فقط نشانی کامل https با نام دامنه (نه IP، نه localhost، نه اطلاعات ورود در نشانی)، حداکثر ۵۰۰ نویسه؛ وگرنه null.
    /// مثال: SafeUrl("https://logo.samandehi.ir/logo.aspx?id=1") ← همان؛ SafeUrl("javascript:alert(1)") ← null؛ SafeUrl("http://x.ir/a.png") ← null.
    /// </summary>
    public static string? SafeUrl(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 500)
            return null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        if (uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        return uri.AbsoluteUri;
    }
}
