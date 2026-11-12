using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo.Feeds;

/// <summary>حالت درخواست API ترب (دقیقاً یکی از این‌ها – طبق مستند رسمی product_api_v3).</summary>
public enum TorobMode : byte
{
    PageUrls = 1,
    PageUniques = 2,
    Paged = 3,
    Cursor = 4,
}

/// <summary>درخواست تجزیه‌شدهٔ ترب.</summary>
public sealed record TorobQuery(TorobMode Mode, IReadOnlyList<string> Values, int Page, string Sort, string? Cursor);

/// <summary>یک محصول در پاسخ ترب (نام فیلدها دقیقاً مطابق مستند ترب).</summary>
public sealed class TorobProduct
{
    [JsonPropertyName("page_unique")] public string PageUnique { get; init; } = "";
    [JsonPropertyName("page_url")] public string PageUrl { get; init; } = "";
    [JsonPropertyName("product_group_id")] public string? ProductGroupId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = "";
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }
    [JsonPropertyName("current_price")] public long CurrentPrice { get; init; }
    [JsonPropertyName("old_price")] public long? OldPrice { get; init; }
    [JsonPropertyName("availability")] public bool Availability { get; init; }
    [JsonPropertyName("category_name")] public string? CategoryName { get; init; }
    [JsonPropertyName("image_links")] public IReadOnlyList<string> ImageLinks { get; init; } = [];
    [JsonPropertyName("short_desc")] public string? ShortDesc { get; init; }
    [JsonPropertyName("spec")] public IReadOnlyDictionary<string, string> Spec { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("guarantee")] public string? Guarantee { get; init; }
    [JsonPropertyName("date_added")] public string DateAdded { get; init; } = "";
    [JsonPropertyName("date_updated")] public string? DateUpdated { get; init; }
}

/// <summary>پاسخ کامل API ترب.</summary>
public sealed class TorobResponse
{
    [JsonPropertyName("api_version")] public string ApiVersion { get; init; } = "torob_api_v3";
    [JsonPropertyName("current_page")] public int CurrentPage { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
    [JsonPropertyName("max_pages")] public int MaxPages { get; init; }
    [JsonPropertyName("next_cursor"), JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? NextCursor { get; init; }
    [JsonPropertyName("products")] public IReadOnlyList<TorobProduct> Products { get; init; } = [];
}

/// <summary>
/// API محصولات ترب نسخهٔ ۳ (مستند رسمی github.com/torob/Torob-Sync) و دادهٔ فید عمومی محصولات.
/// <para>چرا؟ ترب بزرگ‌ترین مقایسه‌گر قیمت ایران است؛ با این API قیمت و موجودی محصولات همیشه درست و فوری در ترب دیده می‌شود
/// (به جای خزش کند صفحه‌ها) و صفحه‌های حذف‌شده خودکار از ترب حذف می‌شوند (در پاسخ نمی‌آیند).</para>
/// <para>کجا استفاده می‌شود؟ <c>POST /torob_api/v3/products</c> (با توکن EdDSA ترب) و <c>/feeds/products.xml</c>.</para>
/// </summary>
public interface ITorobFeedService
{
    /// <summary>اجرای یک درخواست تجزیه‌شده. <paramref name="baseUrl"/> = «https://shop.fratak.ir».</summary>
    Task<TorobResponse> QueryAsync(TorobQuery query, string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>همهٔ محصولات فعال برای فید عمومی (حداکثر <paramref name="max"/>).</summary>
    Task<IReadOnlyList<TorobProduct>> AllAsync(string baseUrl, int max, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ITorobFeedService"/>
public sealed class TorobFeedService(FratakShopDbContext context) : ITorobFeedService
{
    /// <summary>اندازهٔ هر صفحه (ثابت مستند ترب).</summary>
    public const int PageSize = 100;

    /// <summary>بیشترین آدرس/شناسه در یک درخواست تکی.</summary>
    public const int MaxLookups = 100;

    public const string SortDateAdded = "date_added_desc";
    public const string SortDateUpdated = "date_updated_desc";
    public const string SortProductId = "product_id_desc";

    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    /// <summary>
    /// تجزیهٔ بدنهٔ JSON درخواست ترب. خطا ← (null، متن خطا) تا ۴۰۰ با <c>{"error": "..."}</c> برگردد.
    /// <example><code>
    /// Parse("{\"page\":1,\"sort\":\"date_added_desc\"}") ← (Paged, page 1)
    /// Parse("{}")                                         ← (null, "sort parameter is not provided")
    /// </code></example>
    /// </summary>
    public static (TorobQuery? Query, string? Error) Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 200_000)
            return (null, "sort parameter is not provided");
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, "request body must be a JSON object");

            var hasUrls = root.TryGetProperty("page_urls", out var urls);
            var hasUniques = root.TryGetProperty("page_uniques", out var uniques);
            var hasPage = root.TryGetProperty("page", out var page);
            var hasCursor = root.TryGetProperty("cursor", out var cursor);
            var sort = root.TryGetProperty("sort", out var sortValue) && sortValue.ValueKind == JsonValueKind.String ? sortValue.GetString() : null;

            var modes = (hasUrls ? 1 : 0) + (hasUniques ? 1 : 0) + (hasPage || hasCursor || sort is not null ? 1 : 0);
            if (modes > 1)
                return (null, "exactly one of page_urls, page_uniques or page/sort must be provided");

            if (hasUrls || hasUniques)
            {
                var array = hasUrls ? urls : uniques;
                if (array.ValueKind != JsonValueKind.Array)
                    return (null, (hasUrls ? "page_urls" : "page_uniques") + " must be an array");
                var values = array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Where(v => v.Length is > 0 and <= 1500).Distinct().ToList();
                if (values.Count == 0)
                    return (null, (hasUrls ? "page_urls" : "page_uniques") + " is empty");
                if (values.Count > MaxLookups)
                    return (null, $"at most {MaxLookups} items are allowed");
                return (new TorobQuery(hasUrls ? TorobMode.PageUrls : TorobMode.PageUniques, values, 1, "", null), null);
            }

            if (string.IsNullOrWhiteSpace(sort))
                return (null, "sort parameter is not provided");

            if (sort == SortProductId)
            {
                if (hasPage)
                    return (null, "page is not supported with product_id_desc; use cursor");
                string? cursorText = null;
                if (hasCursor && cursor.ValueKind != JsonValueKind.Null)
                {
                    if (cursor.ValueKind != JsonValueKind.String || DecodeCursor(cursor.GetString()) is null)
                        return (null, "invalid cursor");
                    cursorText = cursor.GetString();
                }

                return (new TorobQuery(TorobMode.Cursor, [], 1, sort, cursorText), null);
            }

            if (sort is not (SortDateAdded or SortDateUpdated))
                return (null, "invalid sort parameter");
            if (hasCursor)
                return (null, "cursor is only supported with product_id_desc");
            if (!hasPage || page.ValueKind != JsonValueKind.Number || !page.TryGetInt32(out var number) || number < 1)
                return (null, "page parameter is not provided");
            return (new TorobQuery(TorobMode.Paged, [], number, sort, null), null);
        }
        catch (JsonException)
        {
            return (null, "invalid JSON");
        }
    }

    public async Task<TorobResponse> QueryAsync(TorobQuery query, string baseUrl, CancellationToken cancellationToken = default)
    {
        var products = Live();
        switch (query.Mode)
        {
            case TorobMode.PageUrls:
            {
                var slugs = query.Values.Select(SlugFromUrl).Where(s => s is not null).Select(s => s!).Distinct().ToList();
                var found = await Load(products.Where(p => slugs.Contains(p.Slug)), cancellationToken);
                return Single(found, baseUrl);
            }
            case TorobMode.PageUniques:
            {
                var ids = query.Values.Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
                var found = await Load(products.Where(p => ids.Contains(p.Id)), cancellationToken);
                return Single(found, baseUrl);
            }
            case TorobMode.Paged:
            {
                var total = await products.CountAsync(cancellationToken);
                var ordered = query.Sort == SortDateUpdated
                    ? products.OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Id)
                    : products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id);
                var page = await Load(ordered.Skip((query.Page - 1) * PageSize).Take(PageSize), cancellationToken);
                return new TorobResponse
                {
                    CurrentPage = query.Page,
                    Total = total,
                    MaxPages = MaxPages(total),
                    Products = page.Select(p => Map(p, baseUrl)).ToList(),
                };
            }
            default:
            {
                var total = await products.CountAsync(cancellationToken);
                var offset = DecodeCursor(query.Cursor) ?? 0;
                var page = await Load(products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip(offset).Take(PageSize), cancellationToken);
                var next = offset + page.Count;
                return new TorobResponse
                {
                    CurrentPage = offset / PageSize + 1,
                    Total = total,
                    MaxPages = MaxPages(total),
                    NextCursor = page.Count == PageSize && next < total ? EncodeCursor(next) : null,
                    Products = page.Select(p => Map(p, baseUrl)).ToList(),
                };
            }
        }
    }

    public async Task<IReadOnlyList<TorobProduct>> AllAsync(string baseUrl, int max, CancellationToken cancellationToken = default)
    {
        var page = await Load(Live().OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt).Take(Math.Clamp(max, 1, 50_000)), cancellationToken);
        return page.Select(p => Map(p, baseUrl)).ToList();
    }

    /// <summary>محصولات زنده (حذف‌شده و غیرفعال در پاسخ نمی‌آیند؛ ترب آن‌ها را حذف‌شده می‌داند).</summary>
    private IQueryable<Product> Live() => context.Products.AsNoTracking().Where(p => p.IsActive);

    private static Task<List<Product>> Load(IQueryable<Product> query, CancellationToken cancellationToken) =>
        query.Include(p => p.Images).Include(p => p.Features).Include(p => p.Category).AsSplitQuery().ToListAsync(cancellationToken);

    private static TorobResponse Single(List<Product> found, string baseUrl) => new()
    {
        CurrentPage = 1,
        Total = found.Count,
        MaxPages = found.Count == 0 ? 0 : 1,
        Products = found.Select(p => Map(p, baseUrl)).ToList(),
    };

    public static int MaxPages(int total) => (int)Math.Ceiling(total / (double)PageSize);

    /// <summary>
    /// تبدیل محصول به قالب ترب (ایستا برای تست). قیمت به تومان صحیح؛ ناموجود ← قیمت ۰ و availability=false (قانون ترب).
    /// </summary>
    public static TorobProduct Map(Product product, string baseUrl, DateTimeOffset? now = null)
    {
        var root = baseUrl.TrimEnd('/');
        var moment = now ?? DateTimeOffset.UtcNow;
        var available = product.IsActive && product.StockQuantity > 0;
        var final = product.GetFinalPrice(moment);
        var images = product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder)
            .Select(i => AbsoluteImage(root, i.ImageName)).Where(u => u is not null).Select(u => u!).Distinct().Take(10).ToList();
        var spec = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var feature in product.Features.OrderBy(f => f.DisplayOrder))
        {
            var key = PersianText.Truncate(PersianText.Clean(feature.Key), 200);
            var value = PersianText.Truncate(PersianText.Clean(feature.Value), 500);
            if (key.Length > 0 && value.Length > 0)
                spec.TryAdd(key, value);
        }

        var shortText = PersianText.Clean(PersianText.HtmlToText(product.ShortDescription));
        return new TorobProduct
        {
            PageUnique = product.Id.ToString("N"),
            PageUrl = Cut(root + "/product/" + Uri.EscapeDataString(product.Slug), 1500),
            Title = Cut(product.Name, 500),
            Subtitle = string.IsNullOrWhiteSpace(product.EnglishName) ? null : Cut(product.EnglishName.Trim(), 500),
            CurrentPrice = available ? (long)decimal.Round(final) : 0,
            OldPrice = available && product.HasActiveDiscount(moment) ? (long)decimal.Round(product.Price) : null,
            Availability = available,
            CategoryName = product.Category is null || product.Category.IsSystem ? null : Cut(product.Category.Name, 200),
            ImageLinks = images,
            ShortDesc = shortText.Length == 0 ? null : PersianText.Truncate(shortText, 500),
            Spec = spec,
            Guarantee = string.IsNullOrWhiteSpace(product.WarrantyTitle) ? null : Cut(product.WarrantyTitle.Trim(), 200),
            DateAdded = IranTime(product.PublishedAt ?? product.CreatedAt),
            DateUpdated = IranTime(product.UpdatedAt),
        };
    }

    /// <summary>زمان ISO 8601 با منطقهٔ تهران. مثال: «2026-11-04T10:30:00+03:30».</summary>
    public static string IranTime(DateTimeOffset value) =>
        value.ToOffset(Tehran).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>آدرس مطلق تصویر (همان قاعدهٔ ImageHelper: نام خالی ← آپلود محصولات). نام خالی ← null.</summary>
    public static string? AbsoluteImage(string root, string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
            return null;
        if (imageName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || imageName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return imageName;
        if (imageName.StartsWith('/'))
            return root + imageName;
        return root + "/uploads/products/" + Uri.EscapeDataString(imageName);
    }

    /// <summary>نامک محصول از آدرس صفحه (مطلق یا نسبی). مثال: «https://shop.fratak.ir/product/a55?x=1» ← «a55».</summary>
    public static string? SlugFromUrl(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" ? absolute.AbsolutePath : url.Split('?', '#')[0];
        const string prefix = "/product/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var slug = Uri.UnescapeDataString(path[prefix.Length..].Trim('/'));
        return slug.Length is > 0 and <= 200 && !slug.Contains('/') ? slug : null;
    }

    public static string EncodeCursor(int offset) =>
        Convert.ToBase64String(Encoding.ASCII.GetBytes("o:" + offset.ToString(CultureInfo.InvariantCulture))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>رمزگشایی cursor؛ نامعتبر ← null.</summary>
    public static int? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 40)
            return null;
        try
        {
            var text = Encoding.ASCII.GetString(TorobTokenValidator.FromBase64Url(cursor));
            return text.StartsWith("o:", StringComparison.Ordinal) && int.TryParse(text.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0
                ? offset
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
