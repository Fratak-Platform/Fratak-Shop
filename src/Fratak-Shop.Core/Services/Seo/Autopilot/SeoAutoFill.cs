using System.Globalization;
using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// داده‌های واقعی یک صفحه برای پر کردن فیلدهای «مشتق» سئو.
/// </summary>
public sealed class AutoFillFacts
{
    public SeoEntityKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string? CategoryName { get; init; }
    public string? BrandName { get; init; }
    public string SiteName { get; init; } = "";
    public string? AuthorName { get; init; }
    public string? XHandle { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>کلمات کلیدی رتبه‌بندی‌شده از داده‌های واقعی (<see cref="KeywordIntel.Rank"/>).</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>محصول: موجود است؟ (اولویت نقشهٔ سایت)</summary>
    public bool InStock { get; init; } = true;

    /// <summary>محصول: فروش ۹۰ روز اخیر. دسته/برند: تعداد محصولات فعال.</summary>
    public int Popularity { get; init; }

    /// <summary>صفحه دیگر قابل نمایش نیست (غیرفعال/پیش‌نویس) ← فیلدهای نقشهٔ سایت دست نمی‌خورد.</summary>
    public bool IsLive { get; init; } = true;
}

/// <summary>
/// پر کردن خودکار فیلدهای «مشتق» سئو (موارد ۱ تا ۱۸ پیشنهاد سئو، به‌جز ۱۹ و ۲۰ که حذف شدند).
/// <para>فیلدهای «نوشتنی» (عنوان، توضیحات متا، کلمهٔ کلیدی کانونی، FAQ) را موتور سئو با تحقیق و تنوع می‌نویسد؛ این کلاس
/// بقیه را از همان‌ها و از دادهٔ واقعی می‌سازد: اوپن‌گراف و ایکس، Alt تصویر اشتراک، عنوان بردکرامب، نوع اسکیما، بخش و برچسب
/// مقاله، نویسنده و ناشر، زبان، موضوع، اولویت و دورهٔ تغییر نقشهٔ سایت و کلمات کلیدی متا.</para>
/// <para>قانون طلایی «مُهر»: هر مقداری که سیستم می‌نویسد هش آن در <c>SeoAutoState.StampsJson</c> ذخیره می‌شود.
/// دفعهٔ بعد اگر مقدار فعلی همان هش را داشت یعنی هنوز «مال سیستم» است و می‌تواند به‌روز شود (مثلاً عنوان اوپن‌گراف
/// همراه عنوان اصلی)؛ اگر انسان عوضش کرده بود، دیگر هرگز دست نمی‌خورد.</para>
/// <para>کجا استفاده می‌شود؟ <c>SeoAutopilotService.ProcessAsync</c> بعد از هر ذخیرهٔ صفحه و کار شبانه.</para>
/// </summary>
public static class SeoAutoFill
{
    /// <summary>
    /// اعمال روی <paramref name="seo"/> (درجا). خروجی: نام فیلدهای عوض‌شده و مقدار قبلی آن‌ها (برای تاریخچه).
    /// <paramref name="stamps"/> هم درجا به‌روز می‌شود.
    /// <example><code>
    /// var stamps = new Dictionary&lt;string, string&gt;();
    /// var changed = SeoAutoFill.Apply(product.Seo ??= new(), facts, stamps);
    /// // changed["OgTitle"] == null (قبلاً خالی بود)، product.Seo.OgTitle == product.Seo.Title
    /// </code></example>
    /// </summary>
    public static Dictionary<string, string?> Apply(SeoMetaData seo, AutoFillFacts facts, Dictionary<string, string> stamps)
    {
        var before = new Dictionary<string, string?>(StringComparer.Ordinal);
        var title = Clean(seo.Title) ?? Clean(facts.Name);
        var meta = Clean(seo.MetaDescription);
        var shortName = KeywordPlanner.ShortName(facts.Name);
        var isArticle = facts.Kind == SeoEntityKind.BlogPost;

        void Set(string field, string? current, string? value, Action<string?> assign)
        {
            value = Clean(value);
            if (value is { Length: > 200 })
                value = PersianText.Truncate(value, 199); // سقف ستون‌های متنی SeoMetaData
            if (value is null || string.Equals(current, value, StringComparison.Ordinal))
                return;
            var owned = string.IsNullOrWhiteSpace(current)
                        || stamps.TryGetValue(field, out var stamp) && stamp == Stamp(current);
            if (!owned)
                return;
            before[field] = current;
            assign(value);
            stamps[field] = Stamp(value);
        }

        // ── شبکه‌های اجتماعی (اوپن‌گراف/ایکس) همراه عنوان و توضیحات اصلی ──
        Set(nameof(seo.OgTitle), seo.OgTitle, title, v => seo.OgTitle = v);
        Set(nameof(seo.OgDescription), seo.OgDescription, meta, v => seo.OgDescription = v);
        Set(nameof(seo.XTitle), seo.XTitle, title, v => seo.XTitle = v);
        Set(nameof(seo.XDescription), seo.XDescription, meta, v => seo.XDescription = v);
        Set(nameof(seo.OgType), seo.OgType, facts.Kind switch
        {
            SeoEntityKind.Product => "product",
            SeoEntityKind.BlogPost => "article",
            _ => "website",
        }, v => seo.OgType = v);
        Set(nameof(seo.OgLocale), seo.OgLocale, "fa_IR", v => seo.OgLocale = v);
        Set(nameof(seo.OgSiteName), seo.OgSiteName, facts.SiteName, v => seo.OgSiteName = v);
        Set(nameof(seo.XCard), seo.XCard, "summary_large_image", v => seo.XCard = v);
        if (!string.IsNullOrWhiteSpace(facts.XHandle))
            Set(nameof(seo.XSite), seo.XSite, facts.XHandle, v => seo.XSite = v);
        var imageAlt = ImageAlt(facts);
        Set(nameof(seo.OgImageAlt), seo.OgImageAlt, imageAlt, v => seo.OgImageAlt = v);
        Set(nameof(seo.XImageAlt), seo.XImageAlt, imageAlt, v => seo.XImageAlt = v);
        Set(nameof(seo.SocialImageAlt), seo.SocialImageAlt, imageAlt, v => seo.SocialImageAlt = v);

        // ── ساختار و زبان ──
        Set(nameof(seo.BreadcrumbTitle), seo.BreadcrumbTitle, PersianText.Truncate(shortName.Length >= 3 ? shortName : facts.Name, 40), v => seo.BreadcrumbTitle = v);
        Set(nameof(seo.Language), seo.Language, "fa-IR", v => seo.Language = v);
        Set(nameof(seo.Topic), seo.Topic, facts.CategoryName, v => seo.Topic = v);
        Set(nameof(seo.SchemaType), seo.SchemaType, SchemaType(facts.Kind), v => seo.SchemaType = v);
        Set(nameof(seo.MaxImagePreview), seo.MaxImagePreview, "large", v => seo.MaxImagePreview = v);
        Set(nameof(seo.PublisherName), seo.PublisherName, facts.SiteName, v => seo.PublisherName = v);

        // ── مقاله (مورد ۱۴: نویسنده و ناشر از تنظیمات) ──
        if (isArticle)
        {
            var author = Clean(facts.AuthorName) ?? (facts.SiteName.Length > 0 ? "تیم محتوای " + facts.SiteName : null);
            Set(nameof(seo.AuthorName), seo.AuthorName, author, v => seo.AuthorName = v);
            Set(nameof(seo.ArticleSection), seo.ArticleSection, facts.CategoryName, v => seo.ArticleSection = v);
            var tags = facts.Keywords.Take(6).ToList();
            if (tags.Count > 0)
                Set(nameof(seo.ArticleTagsJson), seo.ArticleTagsJson, JsonSerializer.Serialize(tags), v => seo.ArticleTagsJson = v);
            if (seo.ArticlePublishedTime is null)
            {
                before[nameof(seo.ArticlePublishedTime)] = null;
                seo.ArticlePublishedTime = facts.PublishedAt ?? facts.CreatedAt;
            }

            var modified = facts.UpdatedAt ?? facts.PublishedAt ?? facts.CreatedAt;
            if (seo.ArticleModifiedTime is null || stamps.ContainsKey(nameof(seo.ArticleModifiedTime)) && seo.ArticleModifiedTime < modified)
            {
                before[nameof(seo.ArticleModifiedTime)] = seo.ArticleModifiedTime?.ToString("O", CultureInfo.InvariantCulture);
                seo.ArticleModifiedTime = modified;
                stamps[nameof(seo.ArticleModifiedTime)] = "auto";
            }
        }

        // ── کلمات کلیدی متا (از دادهٔ واقعی؛ بینگ و یاندکس هنوز از آن استفاده می‌کنند) ──
        var keywords = BuildMetaKeywords(seo.FocusKeyword, facts.Keywords);
        if (keywords is not null)
            Set(nameof(seo.MetaKeywords), seo.MetaKeywords, keywords, v => seo.MetaKeywords = v);

        // ── نقشهٔ سایت ──
        if (facts.IsLive)
        {
            var priority = SitemapPriority(facts);
            var stampKey = nameof(seo.SitemapPriority);
            var priorityText = priority.ToString(CultureInfo.InvariantCulture);
            var currentPriority = seo.SitemapPriority?.ToString(CultureInfo.InvariantCulture);
            if (currentPriority != priorityText && (seo.SitemapPriority is null || stamps.TryGetValue(stampKey, out var ps) && ps == Stamp(currentPriority)))
            {
                before[stampKey] = currentPriority;
                seo.SitemapPriority = priority;
                stamps[stampKey] = Stamp(priorityText);
            }

            Set(nameof(seo.SitemapChangeFrequency), seo.SitemapChangeFrequency, ChangeFrequency(facts), v => seo.SitemapChangeFrequency = v);
        }

        return before;
    }

    /// <summary>
    /// کلمات کلیدی متا: کلمهٔ کانونی + حداکثر ۷ عبارت دیگر، بدون تکرار، جدا با «، ». هیچ داده‌ای نباشد ← null.
    /// </summary>
    public static string? BuildMetaKeywords(string? focus, IReadOnlyList<string> keywords)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phrase in new[] { focus }.Concat(keywords))
        {
            var clean = Clean(phrase);
            if (clean is null || clean.Contains(',') || clean.Contains('،'))
                continue;
            if (seen.Add(PersianText.Normalize(clean)))
                list.Add(clean);
            if (list.Count >= 8)
                break;
        }

        return list.Count == 0 ? null : string.Join("، ", list);
    }

    /// <summary>نوع اسکیمای پیش‌فرض هر نوع صفحه.</summary>
    public static string SchemaType(SeoEntityKind kind) => kind switch
    {
        SeoEntityKind.Product => "Product",
        SeoEntityKind.BlogPost => "BlogPosting",
        SeoEntityKind.Category or SeoEntityKind.Brand => "CollectionPage",
        _ => "WebPage",
    };

    /// <summary>
    /// اولویت نقشهٔ سایت از داده: دسته ۰٫۸ (پرمحصول ۰٫۹)، برند ۰٫۶، محصول ۰٫۶ (پرفروش ۰٫۷، ناموجود ۰٫۴)، مقاله ۰٫۵، صفحه ۰٫۳.
    /// </summary>
    public static decimal SitemapPriority(AutoFillFacts facts) => facts.Kind switch
    {
        SeoEntityKind.Category => facts.Popularity >= 20 ? 0.9m : 0.8m,
        SeoEntityKind.Brand => 0.6m,
        SeoEntityKind.Product => !facts.InStock ? 0.4m : facts.Popularity >= 10 ? 0.7m : 0.6m,
        SeoEntityKind.BlogPost => 0.5m,
        _ => 0.3m,
    };

    /// <summary>دورهٔ تغییر: دسته روزانه، محصول هفتگی، مقاله ماهانه، صفحه سالانه.</summary>
    public static string ChangeFrequency(AutoFillFacts facts) => facts.Kind switch
    {
        SeoEntityKind.Category => "daily",
        SeoEntityKind.Product or SeoEntityKind.Brand => "weekly",
        SeoEntityKind.BlogPost => "monthly",
        _ => "yearly",
    };

    /// <summary>Alt تصویر اشتراک: نام + برند (اگر در نام نیست). مثال: «هدفون WH-1000XM5 – سونی».</summary>
    public static string ImageAlt(AutoFillFacts facts)
    {
        var name = Clean(facts.Name) ?? "";
        var brand = Clean(facts.BrandName);
        return brand is not null && !name.Contains(brand, StringComparison.OrdinalIgnoreCase) ? $"{name} – {brand}" : name;
    }

    /// <summary>هش کوتاه یک مقدار (برای مُهر).</summary>
    public static string Stamp(string? value) => PersianText.Hash(value ?? "")[..12];

    private static string? Clean(string? value)
    {
        var clean = PersianText.Clean(value);
        return clean.Length == 0 ? null : clean;
    }
}
