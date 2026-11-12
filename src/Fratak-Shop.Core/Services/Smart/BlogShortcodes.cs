using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// کدهای کوتاه مقاله (B16/C22): <c>[[product:slug]]</c> ← کارت محصول با عکس، قیمت روز و دکمه؛ <c>[[price:slug]]</c> ← قیمت روز.
/// <para>چرا کد کوتاه؟ قیمت و موجودی هر روز عوض می‌شود؛ اگر داخل متن مقاله نوشته شود کهنه و گمراه‌کننده می‌شود.
/// کد کوتاه هنگام نمایش از فروشگاه خوانده می‌شود. کالای حذف‌شده/غیرفعال ← کارت حذف می‌شود (بدون لینک شکسته).</para>
/// <para>امنیت: جایگزینی <b>بعد</b> از پاک‌سازی XSS انجام می‌شود و همهٔ مقدارها HTML-encode می‌شوند؛ نامک فقط حروف کوچک لاتین، عدد و خط تیره.</para>
/// <para>محل استفاده: BlogPost.cshtml.cs (نمایش)، BuyerGuideService (نقشهٔ «مقاله‌های راهنمای این محصول»).</para>
/// </summary>
public static partial class BlogShortcodes
{
    /// <summary>نامک‌های همهٔ کدهای کوتاه متن (بدون تکرار، حداکثر ۳۰). مثال: «… [[product:a-1]] … [[price:b]]» ← [a-1، b].</summary>
    public static List<string> ProductSlugs(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("[[", StringComparison.Ordinal))
            return [];
        try
        {
            return CodeRegex().Matches(html).Select(m => m.Groups["slug"].Value.ToLowerInvariant()).Distinct().Take(30).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    /// <summary>
    /// جایگزینی کدها با HTML. <c>&lt;p&gt;[[product:x]]&lt;/p&gt;</c> کامل جایگزین می‌شود تا div داخل p نیفتد.
    /// کالای ناموجود در فهرست ← کد حذف می‌شود.
    /// </summary>
    public static string Render(string html, IReadOnlyCollection<ShortcodeProduct> products, Func<string?, string> imageUrl)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("[[", StringComparison.Ordinal))
            return html;
        var bySlug = products.GroupBy(p => p.Slug, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        try
        {
            var result = ParagraphCardRegex().Replace(html, m => Card(bySlug, m.Groups["slug"].Value, imageUrl));
            return CodeRegex().Replace(result, m =>
            {
                var slug = m.Groups["slug"].Value;
                return string.Equals(m.Groups["kind"].Value, "price", StringComparison.OrdinalIgnoreCase) ? Price(bySlug, slug) : Card(bySlug, slug, imageUrl);
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return html;
        }
    }

    private static string Price(Dictionary<string, ShortcodeProduct> bySlug, string slug)
    {
        if (!bySlug.TryGetValue(slug, out var p))
            return "—";
        return p.InStock ? $"{Money(p.FinalPrice)} تومان" : "ناموجود";
    }

    private static string Card(Dictionary<string, ShortcodeProduct> bySlug, string slug, Func<string?, string> imageUrl)
    {
        if (!bySlug.TryGetValue(slug, out var p))
            return string.Empty;
        var e = (string s) => WebUtility.HtmlEncode(s);
        var url = "/product/" + Uri.EscapeDataString(p.Slug);
        var builder = new StringBuilder();
        builder.Append("<div class=\"fs-product-card\">");
        builder.Append($"<a class=\"fs-product-card__img\" href=\"{url}\"><img src=\"{e(imageUrl(p.ImageName))}\" alt=\"{e(p.Name)}\" loading=\"lazy\" width=\"96\" height=\"96\"></a>");
        builder.Append("<div class=\"fs-product-card__body\">");
        builder.Append($"<a class=\"fs-product-card__name\" href=\"{url}\">{e(p.Name)}</a>");
        if (p.Reviews >= 3)
            builder.Append($"<span class=\"fs-product-card__rating\">★ {p.Rating.ToString("0.0", CultureInfo.InvariantCulture)} ({p.Reviews.ToString(CultureInfo.InvariantCulture)} نظر)</span>");
        if (p.InStock)
        {
            builder.Append("<span class=\"fs-product-card__price\">");
            if (p.FinalPrice < p.Price)
                builder.Append($"<del>{Money(p.Price)}</del> ");
            builder.Append($"{Money(p.FinalPrice)} تومان</span>");
        }
        else
        {
            builder.Append("<span class=\"fs-product-card__price fs-product-card__price--out\">فعلاً ناموجود</span>");
        }
        builder.Append($"<a class=\"fs-product-card__btn\" href=\"{url}\">{(p.InStock ? "مشاهده و خرید" : "مشاهده و خبرم کن")}</a>");
        builder.Append("</div></div>");
        return builder.ToString();
    }

    private static string Money(decimal value) => decimal.Round(value).ToString("#,0", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[\[(?<kind>product|price):(?<slug>[a-z0-9][a-z0-9\-]{0,199})\]\]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 500)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"<p>\s*\[\[product:(?<slug>[a-z0-9][a-z0-9\-]{0,199})\]\]\s*</p>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 500)]
    private static partial Regex ParagraphCardRegex();
}
