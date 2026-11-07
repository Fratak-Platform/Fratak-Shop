using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>ردیف فهرست صفحه‌های کمپین در پنل.</summary>
public sealed record LandingListItem(Guid Id, string Title, string Slug, bool IsActive, bool ShowHeader, bool ShowFooter, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    string? TemplateKey, DateTimeOffset UpdatedAt, string? UpdatedByName);

/// <summary>ورودی ذخیرهٔ صفحه‌ساز. Html/Css/ProjectJson خام از ویرایشگر می‌آیند و این‌جا پاک‌سازی می‌شوند.</summary>
public sealed record LandingSaveInput(Guid? Id, string Title, string? Slug, string? MetaTitle, string? MetaDescription, string? ImageUrl,
    string? ProjectJson, string? Html, string? Css, bool ShowHeader, bool ShowFooter, bool IsActive, DateTimeOffset? StartAt, DateTimeOffset? EndAt,
    string? TemplateKey);

/// <summary>دادهٔ نمایش عمومی. Ended = زمان کمپین تمام شده (صفحه پیام پایان و لینک فروشگاه نشان می‌دهد، ۴۰۴ نمی‌دهد تا لینک‌های پخش‌شده نسوزند).</summary>
public sealed record LandingView(Guid Id, string Slug, string Title, string? MetaTitle, string? MetaDescription, string? ImageUrl, string Html, string Css,
    bool ShowHeader, bool ShowFooter, bool Ended, DateTimeOffset UpdatedAt, DateTimeOffset? EndAt = null);

/// <summary>
/// صفحه‌ساز کمپین (دور ۲۱): ذخیره، نمایش عمومی و کدهای کوتاه کالا.
/// <para>امنیت: HTML با همان پاک‌ساز متن سایت (بدون اسکریپت، رویداد، فرم و iframe) و CSS با <see cref="LandingCss"/> (محدود به «.fs-lp»)
/// هنگام <b>ذخیره</b> پاک می‌شود؛ صفحهٔ عمومی فقط خروجی امن را نشان می‌دهد و با کش صفحات (Output Cache) سرو می‌شود.</para>
/// <para>کدهای کوتاه (هنگام نمایش با قیمت/موجودی امروز): <c>[[product:slug]]</c> کارت یک کالا؛ <c>[[deals:8]]</c> پرتخفیف‌های موجود؛
/// <c>[[newest:8]]</c> تازه‌ها؛ <c>[[bestsellers:8]]</c> پرفروش‌ها؛ <c>[[category:slug:8]]</c> کالاهای یک دسته. حداکثر ۲۴ کالا در هر کد.</para>
/// <para>محل استفاده: صفحه‌های پنل /admin/campaigns و صفحهٔ عمومی Pages/Campaign.cshtml (/campaign/{slug}). در منوی بالای سایت لینکی ساخته نمی‌شود؛
/// نقشهٔ سایت (SitemapService) صفحه‌های فعال را خودش اضافه می‌کند.</para>
/// </summary>
public interface ILandingPageService
{
    Task<List<LandingListItem>> ListAsync(CancellationToken cancellationToken = default);

    Task<LandingPage?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<LandingView?> GetPublicAsync(string slug, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<OperationResult<Guid>> SaveAsync(LandingSaveInput input, string? actor, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>جایگزینی کدهای فهرستی ([[deals:8]] …) با کدهای تک‌کالا داخل شبکهٔ کارت‌ها.</summary>
    Task<string> ExpandListsAsync(string html, CancellationToken cancellationToken = default);
}

public sealed partial class LandingPageService(FratakShopDbContext context) : ILandingPageService
{
    /// <summary>سقف دادهٔ ویرایشگر (۲ مگابایت) و HTML (۵۰۰ هزار نویسه).</summary>
    public const int MaxProjectLength = 2_000_000;
    public const int MaxHtmlLength = 500_000;

    public Task<List<LandingListItem>> ListAsync(CancellationToken cancellationToken = default) =>
        context.LandingPages.AsNoTracking().OrderByDescending(p => p.UpdatedAt).Take(200)
            .Select(p => new LandingListItem(p.Id, p.Title, p.Slug, p.IsActive, p.ShowHeader, p.ShowFooter, p.StartAt, p.EndAt, p.TemplateKey, p.UpdatedAt, p.UpdatedByName))
            .ToListAsync(cancellationToken);

    public Task<LandingPage?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.LandingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<LandingView?> GetPublicAsync(string slug, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!EnglishSlug.IsEnglish(slug) || slug.Length > 200)
            return null;
        var page = await context.LandingPages.AsNoTracking()
            .Where(p => p.Slug == slug && p.IsActive && (p.StartAt == null || p.StartAt <= now))
            .Select(p => new { p.Id, p.Slug, p.Title, p.MetaTitle, p.MetaDescription, p.ImageUrl, p.Html, p.Css, p.ShowHeader, p.ShowFooter, p.EndAt, p.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (page is null)
            return null;
        return new LandingView(page.Id, page.Slug, page.Title, page.MetaTitle, page.MetaDescription, page.ImageUrl, page.Html ?? "", page.Css ?? "",
            page.ShowHeader, page.ShowFooter, page.EndAt is { } end && end <= now, page.UpdatedAt, page.EndAt);
    }

    public async Task<OperationResult<Guid>> SaveAsync(LandingSaveInput input, string? actor, CancellationToken cancellationToken = default)
    {
        var title = input.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            return OperationResult<Guid>.Error("عنوان صفحه را بنویسید.");
        if ((input.ProjectJson?.Length ?? 0) > MaxProjectLength || (input.Html?.Length ?? 0) > MaxHtmlLength)
            return OperationResult<Guid>.Error("صفحه خیلی بزرگ است؛ چند بخش را کم کنید یا عکس‌ها را از کتابخانهٔ رسانه بیاورید (نه چسباندن مستقیم).");
        if (input.StartAt is { } s && input.EndAt is { } e && e <= s)
            return OperationResult<Guid>.Error("پایان کمپین باید بعد از شروع آن باشد.");

        LandingPage? page = null;
        if (input.Id is { } id)
        {
            page = await context.LandingPages.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (page is null)
                return OperationResult<Guid>.NotFound("صفحه پیدا نشد.");
        }

        var slug = EnglishSlug.IsEnglish(input.Slug?.Trim()) ? input.Slug!.Trim() : EnglishSlug.Prefer(title, input.Slug);
        if (string.IsNullOrWhiteSpace(slug) || slug.Length < 2)
            slug = "campaign";
        var ownId = page?.Id;
        var taken = await context.LandingPages.AsNoTracking().Where(p => p.Id != ownId && p.Slug.StartsWith(slug)).Select(p => p.Slug).ToListAsync(cancellationToken);
        slug = EnglishSlug.Unique(slug, taken.Contains, 200);

        if (page is null)
        {
            page = new LandingPage();
            context.LandingPages.Add(page);
        }
        page.Title = Cut(title, 300)!;
        page.Slug = slug;
        page.MetaTitle = Cut(input.MetaTitle, 300);
        page.MetaDescription = Cut(input.MetaDescription, 400);
        page.ImageUrl = SafeImage(input.ImageUrl);
        page.ProjectJson = string.IsNullOrWhiteSpace(input.ProjectJson) ? page.ProjectJson : input.ProjectJson;
        page.Html = CleanHtml(input.Html);
        page.Css = LandingCss.Sanitize(input.Css);
        page.ShowHeader = input.ShowHeader;
        page.ShowFooter = input.ShowFooter;
        page.IsActive = input.IsActive;
        page.StartAt = input.StartAt;
        page.EndAt = input.EndAt;
        page.TemplateKey = Cut(input.TemplateKey, 40) ?? page.TemplateKey;
        page.UpdatedByName = Cut(actor, 150);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(page.Id, input.IsActive ? $"صفحه ذخیره و منتشر شد: /campaign/{slug}" : "صفحه به‌صورت پیش‌نویس ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await context.LandingPages.AsTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null)
            return OperationResult.NotFound("صفحه پیدا نشد.");
        context.LandingPages.Remove(page);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("صفحه به سطل بازیافت رفت.");
    }

    public async Task<string> ExpandListsAsync(string html, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("[[", StringComparison.Ordinal))
            return html;
        List<Match> matches;
        try
        {
            matches = ListCodeRegex().Matches(html).Take(12).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return html;
        }
        if (matches.Count == 0)
            return html;

        var now = DateTimeOffset.UtcNow;
        var result = new StringBuilder(html.Length + 512);
        var last = 0;
        foreach (var m in matches)
        {
            var kind = m.Groups["kind"].Value.ToLowerInvariant();
            var arg = m.Groups["arg"].Success ? m.Groups["arg"].Value.ToLowerInvariant() : null;
            var count = int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Math.Clamp(n, 1, 24) : 8;
            var query = context.Products.AsNoTracking().Where(p => p.IsActive && p.StockQuantity > 0);
            query = kind switch
            {
                "deals" => query.Where(p => p.Price > 0 && p.DiscountPrice != null && p.DiscountPrice > 0 && p.DiscountPrice < p.Price
                                            && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt >= now))
                    .OrderByDescending(p => (p.Price - p.DiscountPrice!.Value) / p.Price),
                "bestsellers" => query.OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.ViewCount),
                "category" when arg is not null => query.Where(p => p.Category.Slug == arg).OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt),
                _ => query.OrderByDescending(p => p.CreatedAt),
            };
            var slugs = await query.Take(count).Select(p => p.Slug).ToListAsync(cancellationToken);
            result.Append(html, last, m.Index - last);
            if (slugs.Count > 0)
            {
                result.Append("<div class=\"fs-lp-grid\">");
                foreach (var slug in slugs)
                    result.Append("[[product:").Append(slug).Append("]]");
                result.Append("</div>");
            }
            last = m.Index + m.Length;
        }
        result.Append(html, last, html.Length - last);
        return result.ToString();
    }

    /// <summary>
    /// HTML امن صفحه: پاک‌سازی سایت (با نگه داشتن class و شناسهٔ خودکار GrapesJS – <see cref="XssSecurity.SanitizeLanding"/>) + حذف تگ body/head که GrapesJS دور خروجی می‌گذارد.
    /// مثال: «&lt;body id="x"&gt;&lt;h1 onclick="…"&gt;حراج&lt;/h1&gt;&lt;script&gt;…&lt;/script&gt;&lt;/body&gt;» ← «&lt;h1&gt;حراج&lt;/h1&gt;».
    /// </summary>
    public static string CleanHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;
        if (html.Length > MaxHtmlLength)
            html = html[..MaxHtmlLength];
        try
        {
            html = BodyTagRegex().Replace(html, string.Empty);
        }
        catch (RegexMatchTimeoutException)
        {
            // پاک‌ساز اصلی کافی است.
        }
        return html.SanitizeLanding().Trim();
    }

    /// <summary>تصویر اشتراک‌گذاری فقط مسیر محلی /uploads/… (بدون لینک بیرونی).</summary>
    private static string? SafeImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        url = url.Trim();
        return url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) && !url.Contains("..", StringComparison.Ordinal) && url.Length <= 500 ? url : null;
    }

    private static string? Cut(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    [GeneratedRegex(@"\[\[(?:(?<kind>deals|newest|bestsellers)(?::(?<n>\d{1,2}))?|(?<kind>category):(?<arg>[a-z0-9][a-z0-9\-]{0,199}?)(?::(?<n>\d{1,2}))?)\]\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex ListCodeRegex();

    [GeneratedRegex(@"</?(?:body|head|html)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 500)]
    private static partial Regex BodyTagRegex();
}
