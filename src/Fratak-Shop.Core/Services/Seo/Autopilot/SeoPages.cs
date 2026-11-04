using System.Globalization;
using System.Reflection;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// نمای یکسان از «یک صفحهٔ سئوپذیر» (محصول، دسته، برند، مقاله، صفحهٔ محتوا) برای خلبان خودکار؛
/// تا کدهای پر کردن، مرتب‌سازی HTML و برگرداندن برای هر پنج نوع یک بار نوشته شوند.
/// <para>موجودیت با ردیابی (Tracking) بارگذاری می‌شود؛ تغییرها با SaveChanges همان DbContext ذخیره می‌شوند.</para>
/// </summary>
public sealed class SeoPage
{
    private readonly Func<string?> _getBody;
    private readonly Action<string?>? _setBody;
    private readonly Func<SeoMetaData?> _getSeo;
    private readonly Action<SeoMetaData> _setSeo;

    internal SeoPage(SeoEntityKind kind, BaseEntity entity, Func<SeoMetaData?> getSeo, Action<SeoMetaData> setSeo,
        Func<string?> getBody, Action<string?>? setBody)
    {
        Kind = kind;
        Entity = entity;
        _getSeo = getSeo;
        _setSeo = setSeo;
        _getBody = getBody;
        _setBody = setBody;
    }

    public SeoEntityKind Kind { get; }
    public BaseEntity Entity { get; }
    public Guid Id => Entity.Id;
    public string Name { get; init; } = "";
    public string Path { get; init; } = "/";
    public bool IsLive { get; init; }
    public string? CategoryName { get; init; }
    public string? BrandName { get; init; }
    public string? AuthorName { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public bool InStock { get; init; } = true;
    public int Popularity { get; init; }
    public bool SupportsFaq { get; init; }
    public IReadOnlyList<string> FeatureValues { get; init; } = [];

    /// <summary>تصویرهای صفحه که Alt دارند/ندارند (محصول: گالری؛ مقاله: تصویر شاخص).</summary>
    public int ImageCount => Images.Count;
    public int ImagesWithAlt => Images.Count(i => !string.IsNullOrWhiteSpace(i.Get()));

    /// <summary>تصویرها: کلید (برای تاریخچه)، خواندن و نوشتن Alt.</summary>
    internal List<(string Key, Func<string?> Get, Action<string?> Set)> Images { get; } = [];

    public SeoMetaData? Seo => _getSeo();

    /// <summary>SEO صفحه (اگر نبود ساخته می‌شود).</summary>
    public SeoMetaData EnsureSeo()
    {
        if (_getSeo() is { } seo)
            return seo;
        var created = new SeoMetaData();
        _setSeo(created);
        return created;
    }

    public string? Body => _getBody();
    public bool HasBody => _setBody is not null;
    public int BodyMaxLength => HtmlSeoFixer.MaxInputLength;

    public void SetBody(string? html) => _setBody?.Invoke(html);

    /// <summary>
    /// Alt تصویرهای بدون Alt: اولی = نام صفحه، بعدی‌ها «نام – تصویر ۲» … (همه یکتا). خروجی: کلید ← Alt نوشته‌شده.
    /// </summary>
    public Dictionary<string, string> FillMissingAlts()
    {
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = PersianText.Truncate(Name, 200);
        var used = Images.Select(i => i.Get()).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < Images.Count; index++)
        {
            var (key, get, set) = Images[index];
            if (!string.IsNullOrWhiteSpace(get()))
                continue;
            var alt = index == 0 && !used.Contains(name) ? name : $"{name} – تصویر {PersianText.Digits(index + 1)}";
            var counter = index + 1;
            while (used.Contains(alt))
                alt = $"{name} – تصویر {PersianText.Digits(++counter)}";
            used.Add(alt);
            set(alt);
            written[key] = alt;
        }

        return written;
    }

    /// <summary>مقدار یک فیلد برای تاریخچه/برگرداندن (فیلد سئو، «Body» یا «ImageAlt:…»).</summary>
    public string? Get(string field)
    {
        if (field == SeoFieldMap.BodyField)
            return Body;
        var image = Images.FirstOrDefault(i => i.Key == field);
        if (image.Key is not null)
            return image.Get();
        return Seo is { } seo ? SeoFieldMap.Read(seo).GetValueOrDefault(field) : null;
    }

    /// <summary>نوشتن یک فیلد هنگام برگرداندن. فیلد ناشناخته ← false.</summary>
    public bool Set(string field, string? value)
    {
        if (field == SeoFieldMap.BodyField)
        {
            if (!HasBody)
                return false;
            SetBody(value);
            return true;
        }

        var image = Images.FirstOrDefault(i => i.Key == field);
        if (image.Key is not null)
        {
            image.Set(value);
            return true;
        }

        return SeoFieldMap.Write(EnsureSeo(), field, value);
    }
}

/// <summary>بارگذاری <see cref="SeoPage"/> برای هر نوع صفحه.</summary>
public static class SeoPages
{
    /// <summary>صفحه با ردیابی؛ حذف‌شده یا ناموجود ← null.</summary>
    public static async Task<SeoPage?> LoadAsync(FratakShopDbContext context, SeoEntityKind kind, Guid id, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case SeoEntityKind.Product:
            {
                var p = await context.Products.AsTracking()
                    .Include(x => x.Images).Include(x => x.Category).Include(x => x.Brand).Include(x => x.Features)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (p is null)
                    return null;
                var page = new SeoPage(kind, p, () => p.Seo, s => p.Seo = s, () => p.Description, v => p.Description = v)
                {
                    Name = p.Name,
                    Path = "/product/" + p.Slug,
                    IsLive = p.IsActive,
                    CategoryName = p.Category?.Name,
                    BrandName = p.Brand?.Name,
                    PublishedAt = p.PublishedAt,
                    InStock = p.StockQuantity > 0,
                    Popularity = p.SalesCount,
                    SupportsFaq = true,
                    FeatureValues = p.Features.Select(f => f.Value).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList(),
                };
                foreach (var image in p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder))
                    page.Images.Add(("ImageAlt:" + image.Id.ToString("N"), () => image.AltText, v => image.AltText = v));
                return page;
            }
            case SeoEntityKind.BlogPost:
            {
                var b = await context.BlogPosts.AsTracking().Include(x => x.BlogCategory).Include(x => x.Author)
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (b is null)
                    return null;
                var author = b.Author is null ? null : $"{b.Author.FirstName} {b.Author.LastName}".Trim();
                var page = new SeoPage(kind, b, () => b.Seo, s => b.Seo = s, () => b.Content, v => b.Content = v ?? "")
                {
                    Name = b.Title,
                    Path = "/blog/" + b.Slug,
                    IsLive = b.IsPublished,
                    CategoryName = b.BlogCategory?.Name,
                    AuthorName = string.IsNullOrWhiteSpace(author) ? null : author,
                    PublishedAt = b.PublishedAt,
                    SupportsFaq = true,
                };
                if (!string.IsNullOrWhiteSpace(b.ImageName))
                    page.Images.Add(("BlogImageAlt", () => b.ImageAlt, v => b.ImageAlt = v));
                return page;
            }
            case SeoEntityKind.Category:
            {
                var c = await context.Categories.AsTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (c is null)
                    return null;
                var count = await context.Products.CountAsync(p => p.CategoryId == id && p.IsActive, cancellationToken);
                return new SeoPage(kind, c, () => c.Seo, s => c.Seo = s, () => c.Description, v => c.Description = v)
                {
                    Name = c.Name,
                    Path = "/category/" + c.Slug,
                    IsLive = c.IsActive,
                    CategoryName = c.Name,
                    Popularity = count,
                    SupportsFaq = true,
                };
            }
            case SeoEntityKind.Brand:
            {
                var b = await context.Brands.AsTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (b is null)
                    return null;
                var count = await context.Products.CountAsync(p => p.BrandId == id && p.IsActive, cancellationToken);
                return new SeoPage(kind, b, () => b.Seo, s => b.Seo = s, () => b.Description, v => b.Description = v)
                {
                    Name = b.Name,
                    Path = "/brand/" + b.Slug,
                    IsLive = b.IsActive,
                    BrandName = b.Name,
                    Popularity = count,
                };
            }
            case SeoEntityKind.ContentPage:
            {
                var c = await context.ContentPages.AsTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (c is null)
                    return null;
                return new SeoPage(kind, c, () => c.Seo, s => c.Seo = s, () => c.Content, v => c.Content = v)
                {
                    Name = c.Title,
                    Path = "/page/" + c.Slug,
                    IsLive = c.IsActive,
                };
            }
            default:
                return null;
        }
    }
}

/// <summary>
/// خواندن/نوشتن فیلدهای <see cref="SeoMetaData"/> به صورت «نام ← متن» (برای تاریخچهٔ قبل/بعد و برگرداندن).
/// عددها و تاریخ‌ها با فرهنگ ثابت (Invariant) و تاریخ با قالب «O» ذخیره می‌شوند.
/// </summary>
public static class SeoFieldMap
{
    /// <summary>نام فیلد ویژهٔ «متن صفحه» در تاریخچه.</summary>
    public const string BodyField = "Body";

    private static readonly Dictionary<string, PropertyInfo> Properties = typeof(SeoMetaData)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite)
        .ToDictionary(p => p.Name, StringComparer.Ordinal);

    /// <summary>همهٔ فیلدهای پرشده به متن.</summary>
    public static Dictionary<string, string?> Read(SeoMetaData seo)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, property) in Properties)
        {
            var value = property.GetValue(seo);
            map[name] = value switch
            {
                null => null,
                string s => s,
                DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                _ => value.ToString(),
            };
        }

        return map;
    }

    /// <summary>نوشتن یک فیلد از متن (null = خالی). نوع ناسازگار یا فیلد ناشناخته ← false.</summary>
    public static bool Write(SeoMetaData seo, string field, string? value)
    {
        if (!Properties.TryGetValue(field, out var property))
            return false;
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        try
        {
            object? parsed = value is null ? null
                : type == typeof(string) ? value
                : type == typeof(DateTimeOffset) ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                : type == typeof(decimal) ? decimal.Parse(value, CultureInfo.InvariantCulture)
                : type == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : type == typeof(bool) ? bool.Parse(value)
                : type == typeof(Guid) ? Guid.Parse(value)
                : null;
            if (parsed is null && value is not null)
                return false;
            if (parsed is null && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
                return false;
            property.SetValue(seo, parsed);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
