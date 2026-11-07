using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>یک ردیف فهرست طرح‌ها در پنل (بدون JSON سنگین).</summary>
public sealed record DesignListItem(Guid Id, string Title, DesignKind Kind, DesignStatus Status, bool IsSmart, string PresetKey, int Width, int Height,
    string? ImageName, string? MobileImageName, string? IdeaNote, string? LinkUrl, Guid? BannerId, Guid? BlogPostId, DateTimeOffset UpdatedAt, DateTimeOffset? ExpiresAt);

/// <summary>نتیجهٔ ذخیرهٔ طرح: فایل‌هایی که دیگر استفاده نمی‌شوند (لایهٔ وب از دیسک پاکشان می‌کند).</summary>
public sealed record DesignSaveResult(Guid Id, IReadOnlyList<string> ReplacedFiles);

/// <summary>
/// دادهٔ استودیوی طراحی (دور ۲۱): فهرست، ذخیره، ایده، رد، انتشار به‌عنوان بنر و کاور مقاله.
/// ساخت تصویر و ذخیرهٔ فایل در لایهٔ وب است (Web/Infrastructure/Design/DesignStudio.cs)؛ این سرویس فقط پایگاه داده را می‌شناسد.
/// <para>مثال: مدیر ایدهٔ «پیشنهاد ویژه» را تأیید می‌کند ← <see cref="PublishBannerAsync"/> یک بنر فعال در «اسلایدر اصلی» با همان تصویر، لینک و
/// متن جایگزین می‌سازد و طرح «منتشرشده» می‌شود؛ ویرایش بعدی طرح عکس همان بنر را هم عوض می‌کند.</para>
/// </summary>
public interface IDesignService
{
    Task<List<DesignListItem>> ListAsync(DesignStatus? status, DesignKind? kind, int take = 60, CancellationToken cancellationToken = default);

    Task<DesignDoc?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>آخرین طرح کاور یک مقاله (برای دکمهٔ «ویرایش طرح کاور»).</summary>
    Task<DesignDoc?> ForPostAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>کلیدهای ایده‌ای که قبلاً ساخته شده‌اند (هر وضعیتی، حتی ردشده) ← دوباره پیشنهاد نمی‌شوند.</summary>
    Task<HashSet<string>> ExistingIdeaKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>کالاها و دسته‌های نامزد ایده (فقط فعال، موجود و دارای عکس). ابعاد عکس صفر است؛ لایهٔ وب از فایل می‌خواند.</summary>
    Task<IdeaInput> IdeaInputAsync(string siteName, CancellationToken cancellationToken = default);

    Task<Guid> AddAsync(DesignDoc doc, CancellationToken cancellationToken = default);

    /// <summary>
    /// ذخیرهٔ ویرایش: JSON و تصویر تازه. اگر طرح به بنر/مقاله وصل باشد، عکس آن‌ها هم به‌روز می‌شود.
    /// خروجی: نام فایل‌های قبلی که دیگر جایی استفاده نمی‌شوند.
    /// </summary>
    Task<OperationResult<DesignSaveResult>> SaveAsync(Guid id, string title, string json, string? mobileJson, string? imageName, string? mobileImageName,
        string? headline, string? actor, CancellationToken cancellationToken = default);

    Task<OperationResult> RejectAsync(Guid id, string? actor, CancellationToken cancellationToken = default);

    /// <summary>حذف نرم طرح. طرح منتشرشده حذف نمی‌شود (اول بنر را غیرفعال کنید). خروجی: فایل‌هایی که می‌شود پاک کرد.</summary>
    Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>انتشار طرح بنر: ساخت (یا به‌روزرسانی) بنر فعال در جایگاه داده‌شده.</summary>
    Task<OperationResult<Guid>> PublishBannerAsync(Guid id, BannerPosition position, string? linkUrl, string? buttonText, string? actor,
        CancellationToken cancellationToken = default);

    /// <summary>گذاشتن تصویر طرح به‌عنوان کاور مقاله. خروجی: نام کاور قبلی مقاله (اگر فایل جدا بود، پاک‌شدنی است).</summary>
    Task<OperationResult<string?>> AttachCoverAsync(Guid id, Guid postId, string? actor, CancellationToken cancellationToken = default);

    /// <summary>تعداد ایده‌های منتظر تأیید (نشان منو و داشبورد).</summary>
    Task<int> PendingIdeasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// عکس کالاهای خود فروشگاه برای پنل «افزودن عکس» ویرایشگر (جست‌وجو در نام کالا؛ خالی = پربازدیدها).
    /// مثال: «هدفون» ← [(«/uploads/products/a.webp»، «هدفون بی‌سیم X»)، …].
    /// </summary>
    Task<List<(string Url, string Title)>> ProductImagesAsync(string? query, int take = 24, CancellationToken cancellationToken = default);
}

public sealed class DesignService(FratakShopDbContext context) : IDesignService
{
    /// <summary>سقف حجم JSON یک طرح (۱ مگابایت). تصویرها باید آدرس محلی باشند، نه base64.</summary>
    public const int MaxJsonLength = 1_000_000;

    public Task<List<DesignListItem>> ListAsync(DesignStatus? status, DesignKind? kind, int take = 60, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var query = context.DesignDocs.AsNoTracking();
        if (status is { } s)
            query = query.Where(d => d.Status == s);
        if (kind is { } k)
            query = query.Where(d => d.Kind == k);
        if (status == DesignStatus.Suggested)
            query = query.Where(d => d.ExpiresAt == null || d.ExpiresAt > now);
        return query.OrderByDescending(d => d.UpdatedAt).Take(Math.Clamp(take, 1, 200))
            .Select(d => new DesignListItem(d.Id, d.Title, d.Kind, d.Status, d.IsSmart, d.PresetKey, d.Width, d.Height, d.ImageName, d.MobileImageName,
                d.IdeaNote, d.LinkUrl, d.BannerId, d.BlogPostId, d.UpdatedAt, d.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    public Task<DesignDoc?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.DesignDocs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DesignDoc?> ForPostAsync(Guid postId, CancellationToken cancellationToken = default) =>
        context.DesignDocs.AsNoTracking().Where(d => d.BlogPostId == postId).OrderByDescending(d => d.UpdatedAt).FirstOrDefaultAsync(cancellationToken);

    public async Task<HashSet<string>> ExistingIdeaKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(50).ToList();
        if (list.Count == 0)
            return [];
        // IgnoreQueryFilters: ایدهٔ حذف‌شده هم «قبلاً ساخته شده» حساب می‌شود (مدیر نخواسته، دوباره نیاید).
        var found = await context.DesignDocs.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.IdeaKey != null && list.Contains(d.IdeaKey))
            .Select(d => d.IdeaKey!)
            .ToListAsync(cancellationToken);
        return found.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IdeaInput> IdeaInputAsync(string siteName, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        // فقط ۲۵۰ کالای مهم (پربازدید/پرفروش/تازه/پرتخفیف) – پرس‌وجوی سبک حتی با ده‌ها هزار کالا.
        var rows = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0 && p.Images.Any())
            .OrderByDescending(p => p.SalesCount * 5 + p.ViewCount)
            .Take(200)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity, p.ViewCount, p.SalesCount, p.CreatedAt,
                p.CategoryId,
                CategoryName = p.Category.IsSystem ? null : p.Category.Name,
                CategorySlug = p.Category.IsSystem ? null : p.Category.Slug,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var recent = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0 && p.Images.Any() && p.CreatedAt >= now.AddDays(-21))
            .OrderByDescending(p => p.CreatedAt).Take(20)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity, p.ViewCount, p.SalesCount, p.CreatedAt,
                p.CategoryId,
                CategoryName = p.Category.IsSystem ? null : p.Category.Name,
                CategorySlug = p.Category.IsSystem ? null : p.Category.Slug,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var products = rows.Concat(recent).DistinctBy(r => r.Id).Select(r =>
        {
            var discounted = r.DiscountPrice is > 0 && r.DiscountPrice < r.Price
                             && (r.DiscountStartAt == null || r.DiscountStartAt <= now) && (r.DiscountEndAt == null || r.DiscountEndAt >= now);
            return (r.CategoryId, Product: new IdeaProduct(r.Id, r.Name, r.Slug, r.Image is null ? null : "/uploads/products/" + r.Image, 0, 0,
                r.Price, discounted ? r.DiscountPrice!.Value : r.Price, r.StockQuantity, r.ViewCount, r.SalesCount, r.CategoryName, r.CategorySlug, r.CreatedAt));
        }).ToList();

        var categoryCounts = await context.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 0 && p.Category.IsActive && !p.Category.IsSystem)
            .GroupBy(p => new { p.CategoryId, p.Category.Name, p.Category.Slug })
            .Select(g => new { g.Key.CategoryId, g.Key.Name, g.Key.Slug, Count = g.Count() })
            .OrderByDescending(g => g.Count).Take(10)
            .ToListAsync(cancellationToken);
        var categories = categoryCounts.Select(c => new IdeaCategory(c.CategoryId, c.Name, c.Slug, c.Count,
            products.Where(p => p.CategoryId == c.CategoryId).Select(p => p.Product).OrderByDescending(p => p.Sales).ThenByDescending(p => p.Views).FirstOrDefault()))
            .ToList();

        return new IdeaInput(products.Select(p => p.Product).ToList(), categories, siteName);
    }

    public async Task<Guid> AddAsync(DesignDoc doc, CancellationToken cancellationToken = default)
    {
        doc.Title = Cut(doc.Title, 200) ?? "طرح";
        doc.Json = doc.Json.Length > MaxJsonLength ? "{}" : doc.Json;
        context.DesignDocs.Add(doc);
        await context.SaveChangesAsync(cancellationToken);
        return doc.Id;
    }

    public async Task<OperationResult<DesignSaveResult>> SaveAsync(Guid id, string title, string json, string? mobileJson, string? imageName, string? mobileImageName,
        string? headline, string? actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength || (mobileJson?.Length ?? 0) > MaxJsonLength)
            return OperationResult<DesignSaveResult>.Error("طرح خیلی بزرگ است (حداکثر ۱ مگابایت). عکس‌ها را از «افزودن عکس» بیاورید، نه چسباندن مستقیم.");
        var doc = await context.DesignDocs.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc is null)
            return OperationResult<DesignSaveResult>.NotFound("طرح پیدا نشد.");

        var replaced = new List<string>();
        doc.Title = Cut(title, 200) ?? doc.Title;
        doc.Json = json;
        if (mobileJson is not null)
            doc.MobileJson = mobileJson;
        if (!string.IsNullOrWhiteSpace(headline))
            doc.Headline = Cut(headline, 300);
        if (!string.IsNullOrWhiteSpace(imageName) && imageName != doc.ImageName)
        {
            if (doc.ImageName is not null)
                replaced.Add(doc.ImageName);
            doc.ImageName = imageName;
        }
        if (!string.IsNullOrWhiteSpace(mobileImageName) && mobileImageName != doc.MobileImageName)
        {
            if (doc.MobileImageName is not null)
                replaced.Add(doc.MobileImageName);
            doc.MobileImageName = mobileImageName;
        }
        if (doc.Status is DesignStatus.Suggested or DesignStatus.Rejected)
            doc.Status = DesignStatus.Draft;
        doc.UpdatedByName = Cut(actor, 150);

        // طرح منتشرشده: بنر/مقالهٔ وصل‌شده همان لحظه عکس تازه را می‌گیرد.
        if (doc.BannerId is { } bannerId && await context.Banners.AsTracking().FirstOrDefaultAsync(b => b.Id == bannerId, cancellationToken) is { } banner)
        {
            banner.ImageName = doc.ImageName ?? banner.ImageName;
            if (doc.MobileImageName is not null && banner.Position == BannerPosition.HeroSlider)
                banner.MobileImageName = doc.MobileImageName;
            doc.Status = DesignStatus.Published;
        }
        if (doc.BlogPostId is { } postId && doc.ImageName is not null
            && await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == postId, cancellationToken) is { } post && post.ImageName != doc.ImageName)
        {
            // کاور قبلی مقاله فقط اگر خروجی همین طرح بوده پاک‌شدنی است (عکس دستی مدیر دست نمی‌خورد).
            post.ImageName = doc.ImageName;
            doc.Status = DesignStatus.Published;
        }
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<DesignSaveResult>.Success(new DesignSaveResult(doc.Id, replaced));
    }

    public async Task<OperationResult> RejectAsync(Guid id, string? actor, CancellationToken cancellationToken = default)
    {
        var doc = await context.DesignDocs.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc is null)
            return OperationResult.NotFound("طرح پیدا نشد.");
        if (doc.Status == DesignStatus.Published)
            return OperationResult.Error("طرح منتشرشده را نمی‌شود رد کرد؛ اول بنر را غیرفعال کنید.");
        doc.Status = DesignStatus.Rejected;
        doc.UpdatedByName = Cut(actor, 150);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("ایده رد شد و دوباره پیشنهاد نمی‌شود.");
    }

    public async Task<OperationResult<IReadOnlyList<string>>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var doc = await context.DesignDocs.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc is null)
            return OperationResult<IReadOnlyList<string>>.NotFound("طرح پیدا نشد.");
        if (doc.Status == DesignStatus.Published || doc.BannerId is not null)
            return OperationResult<IReadOnlyList<string>>.Error("این طرح روی سایت است (بنر یا کاور مقاله)؛ حذف نمی‌شود.");
        context.DesignDocs.Remove(doc);
        await context.SaveChangesAsync(cancellationToken);
        var files = new[] { doc.ImageName, doc.MobileImageName }.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).ToList();
        return OperationResult<IReadOnlyList<string>>.Success(files);
    }

    public async Task<OperationResult<Guid>> PublishBannerAsync(Guid id, BannerPosition position, string? linkUrl, string? buttonText, string? actor,
        CancellationToken cancellationToken = default)
    {
        var doc = await context.DesignDocs.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc is null)
            return OperationResult<Guid>.NotFound("طرح پیدا نشد.");
        if (doc.Kind != DesignKind.Banner || string.IsNullOrWhiteSpace(doc.ImageName))
            return OperationResult<Guid>.Error("فقط طرح بنرِ دارای تصویر قابل انتشار است.");
        if (position == BannerPosition.None)
            position = DesignPresets.Find(doc.PresetKey)?.Position ?? BannerPosition.HeroSlider;
        var link = SafeLink(linkUrl) ?? SafeLink(doc.LinkUrl);

        Banner? banner = doc.BannerId is { } existing ? await context.Banners.AsTracking().FirstOrDefaultAsync(b => b.Id == existing, cancellationToken) : null;
        if (banner is null)
        {
            var order = await context.Banners.Where(b => b.Position == position).Select(b => (int?)b.DisplayOrder).MinAsync(cancellationToken) ?? 1;
            banner = new Banner { Position = position, DisplayOrder = order - 1 };
            context.Banners.Add(banner);
        }
        banner.ImageName = doc.ImageName!;
        banner.MobileImageName = position == BannerPosition.HeroSlider ? doc.MobileImageName : null;
        banner.Title = Cut(doc.Headline ?? doc.Title, 250);
        banner.Subtitle = Cut(doc.Subline, 500);
        banner.ImageAlt = Cut(doc.Headline ?? doc.Title, 250);
        banner.ButtonText = Cut(string.IsNullOrWhiteSpace(buttonText) ? doc.ButtonText : buttonText, 100);
        banner.LinkUrl = link;
        banner.Position = position;
        banner.IsActive = true;
        banner.StartAt = null;
        banner.EndAt = doc.ExpiresAt;

        doc.LinkUrl = link;
        doc.Status = DesignStatus.Published;
        doc.UpdatedByName = Cut(actor, 150);
        // شناسهٔ Guid هنگام Add در خود برنامه ساخته می‌شود؛ یک SaveChanges کافی است (اتمیک).
        doc.BannerId = banner.Id;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<Guid>.Success(banner.Id, "بنر روی سایت قرار گرفت.");
    }

    public async Task<OperationResult<string?>> AttachCoverAsync(Guid id, Guid postId, string? actor, CancellationToken cancellationToken = default)
    {
        var doc = await context.DesignDocs.AsTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc is null || string.IsNullOrWhiteSpace(doc.ImageName))
            return OperationResult<string?>.NotFound("طرح یا تصویرش پیدا نشد.");
        // فایل کاور در پوشهٔ «blogs» ذخیره می‌شود؛ طرح بنر (پوشهٔ banners) نباید کاور شود.
        if (doc.Kind != DesignKind.BlogCover)
            return OperationResult<string?>.Error("فقط طرح «کاور مقاله» را می‌شود روی مقاله گذاشت.");
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);
        if (post is null)
            return OperationResult<string?>.NotFound("مقاله پیدا نشد.");
        var old = post.ImageName != doc.ImageName ? post.ImageName : null;
        post.ImageName = doc.ImageName;
        if (string.IsNullOrWhiteSpace(post.ImageAlt))
            post.ImageAlt = Cut(post.Title, 250);
        doc.BlogPostId = postId;
        doc.Status = DesignStatus.Published;
        doc.UpdatedByName = Cut(actor, 150);
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult<string?>.Success(old, "کاور مقاله ثبت شد.");
    }

    public Task<int> PendingIdeasAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return context.DesignDocs.CountAsync(d => d.Status == DesignStatus.Suggested && (d.ExpiresAt == null || d.ExpiresAt > now), cancellationToken);
    }

    public async Task<List<(string Url, string Title)>> ProductImagesAsync(string? query, int take = 24, CancellationToken cancellationToken = default)
    {
        var products = context.Products.AsNoTracking().Where(p => p.IsActive && p.Images.Any());
        var q = query?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            q = q.Length > 60 ? q[..60] : q;
            products = products.Where(p => p.Name.Contains(q));
        }
        var rows = await products
            .OrderByDescending(p => p.ViewCount)
            .Take(Math.Clamp(take, 1, 60))
            .Select(p => new
            {
                p.Name,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.Where(r => r.Image is not null).Select(r => ("/uploads/products/" + r.Image, r.Name)).ToList();
    }

    /// <summary>
    /// لینک امن بنر: فقط مسیر داخلی («/…» ولی نه «//…») یا https. مثال: «/product/a» ✓؛ «javascript:…» ✗؛ «//evil.com» ✗.
    /// </summary>
    public static string? SafeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        url = url.Trim();
        if (url.Length > 500)
            return null;
        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal))
            return url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;
    }

    private static string? Cut(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }
}
