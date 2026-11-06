using System.Text.Json;
using System.Text.Json.Serialization;
using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Core.Services.Seo.Engine;

// ═══════════════════════════════════════════════════════════════════════════
//  مدل‌های مشترک موتور سئو
//  • SeoSubject  : «همه چیزی که موتور دربارهٔ یک صفحه می‌داند» (عنوان، متن، تصویرها، واقعیت‌های محصول/دسته …).
//                  SeoEngineService آن را از دیتابیس می‌سازد؛ تحلیل‌گر و نویسنده فقط با همین مدل کار می‌کنند
//                  (پس بدون دیتابیس قابل تست‌اند).
//  • SeoProposal : مقدارهای پیشنهادی یک پیشنهاد (در ستون ProposalJson ذخیره می‌شود).
//  • SeoSiteIndex: نمای کل سایت برای بررسی‌های «تکراری» (عنوان/توضیحات تکراری، رقابت دو صفحه سر یک کلمه).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک تصویر صفحه. Id برای تصویرهای محصول (برای اعمال alt) پر است.</summary>
public sealed record SeoImageInfo(Guid? Id, string? Alt, bool IsPrimary);

/// <summary>یک ویژگی فنی محصول. مثال: («حافظهٔ داخلی»، «۲۵۶ گیگابایت»).</summary>
public sealed record SeoFeature(string Key, string Value);

/// <summary>یک پرسش‌وپاسخ. نام‌های کوتاه JSON (q/a) برای کم‌حجم ماندن ستون FaqJson.</summary>
public sealed record SeoFaqItem([property: JsonPropertyName("q")] string Question, [property: JsonPropertyName("a")] string Answer);

/// <summary>لینک داخلی که نویسنده می‌تواند در متن بگذارد. مثال: («هدفون بی‌سیم»، «/category/هدفون-بی-سیم»).</summary>
public sealed record SeoLink(string Text, string Path);

/// <summary>واقعیت‌های یک محصول که متن تولیدی فقط از آن‌ها استفاده می‌کند (هیچ ادعای ساختگی).</summary>
public sealed class ProductFacts
{
    public string? CategoryName { get; init; }
    public string? CategorySlug { get; init; }
    public string? BrandName { get; init; }
    public string? BrandEnglishName { get; init; }
    public string? BrandSlug { get; init; }
    public IReadOnlyList<SeoFeature> Features { get; init; } = [];
    public string? WarrantyTitle { get; init; }
    public int? WarrantyMonths { get; init; }
    public bool InStock { get; init; }
    public bool HasDiscount { get; init; }
}

/// <summary>واقعیت‌های جمعی یک دسته یا برند (از روی محصولات فعالش).</summary>
public sealed class ListingFacts
{
    public int ProductCount { get; init; }
    public string? ParentName { get; init; }
    /// <summary>برندهای پرمحصول (برای دسته) یا دسته‌های اصلی (برای برند).</summary>
    public IReadOnlyList<SeoLink> RelatedGroups { get; init; } = [];
    /// <summary>زیردسته‌ها (فقط دسته).</summary>
    public IReadOnlyList<SeoLink> SubCategories { get; init; } = [];
    /// <summary>پرفروش‌ترین/پربازدیدترین محصولات.</summary>
    public IReadOnlyList<SeoLink> TopProducts { get; init; } = [];
    /// <summary>ویژگی‌هایی که بیشتر محصولات این گروه دارند (برای راهنمای خرید). مثال: «ظرفیت باتری».</summary>
    public IReadOnlyList<string> CommonFeatureKeys { get; init; } = [];
    /// <summary>درصد محصولاتی که گارانتی دارند (۰ تا ۱۰۰).</summary>
    public int WarrantyPercent { get; init; }
}

/// <summary>اطلاعات کلی فروشگاه که در متن‌ها و پرسش‌وپاسخ استفاده می‌شود (همه از تنظیمات).</summary>
public sealed class StoreFacts
{
    public string SiteName { get; init; } = "";
    /// <summary>معرفی کوتاه فروشگاه (تنظیمات «توضیحات سایت»).</summary>
    public string? Tagline { get; init; }
    /// <summary>طول پسوندی که قالب به عنوان اضافه می‌کند: « | نام‌سایت».</summary>
    public int TitleSuffixLength { get; init; }
    public bool ReturnEnabled { get; init; }
    public int ReturnDays { get; init; }
    public int DeliveryMinDays { get; init; }
    public int DeliveryMaxDays { get; init; }
    public decimal FreeShippingThreshold { get; init; }
    public bool CashOnDelivery { get; init; }

    /// <summary>حداکثر طول عنوان صفحه (بدون نام سایت) تا عنوان کامل از ۶۰ نویسه بیشتر نشود (حداقل ۳۰).</summary>
    public int TitleBudget => Math.Max(30, SeoLimits.TitleMax - TitleSuffixLength);
}

/// <summary>همه چیزی که موتور دربارهٔ یک صفحه می‌داند.</summary>
public sealed class SeoSubject
{
    public SeoEntityKind Kind { get; init; }
    public Guid EntityId { get; init; }

    /// <summary>نام صفحه (نام محصول، عنوان مقاله …).</summary>
    public string Name { get; init; } = "";
    public string? EnglishName { get; init; }
    public string Slug { get; init; } = "";

    /// <summary>پیشوند آدرس. مثال: «/product/».</summary>
    public string PathPrefix { get; init; } = "/";
    public string Path => PathPrefix + Slug;

    // مقدارهای فعلی سئو (null یعنی «خالی، قالب مقدار پیش‌فرض را نشان می‌دهد»)
    public string? SeoTitle { get; init; }
    public string? MetaDescription { get; init; }
    public string? FocusKeyword { get; init; }
    public string? FaqJson { get; init; }
    public bool NoIndex { get; init; }
    public bool HasCanonicalElsewhere { get; init; }

    /// <summary>عنوانی که قالب وقتی عنوان سئو خالی است نشان می‌دهد. مثال برای دسته: «خرید هدفون».</summary>
    public string DefaultTitle { get; init; } = "";

    /// <summary>توضیحاتی که قالب وقتی توضیحات متا خالی است نشان می‌دهد (خلاصه/چکیدهٔ متن).</summary>
    public string? DefaultDescription { get; init; }

    /// <summary>متن اصلی صفحه (HTML).</summary>
    public string? BodyHtml { get; init; }

    /// <summary>متن کوتاه بالای صفحه (توضیح کوتاه محصول، خلاصهٔ مقاله).</summary>
    public string? ShortText { get; init; }

    public IReadOnlyList<SeoImageInfo> Images { get; init; } = [];

    public int Views { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public ProductFacts? Product { get; init; }
    public ListingFacts? Listing { get; init; }

    /// <summary>صفحه‌های مرتبط برای لینک داخلی (مثلاً دستهٔ محصول).</summary>
    public IReadOnlyList<SeoLink> RelatedLinks { get; init; } = [];

    /// <summary>عنوان مؤثر (همان که در تگ title می‌آید، بدون نام سایت).</summary>
    public string EffectiveTitle => string.IsNullOrWhiteSpace(SeoTitle) ? DefaultTitle : SeoTitle!;

    /// <summary>توضیحات مؤثر (همان که در متا می‌آید).</summary>
    public string EffectiveDescription => PersianText.Clean(string.IsNullOrWhiteSpace(MetaDescription)
        ? PersianText.HtmlToText(DefaultDescription)
        : MetaDescription);

    /// <summary>پرسش‌وپاسخ فعلی (JSON خراب → خالی).</summary>
    public IReadOnlyList<SeoFaqItem> Faq => SeoFaq.Parse(FaqJson);

    /// <summary>
    /// هش محتوا: اگر هرکدام از بخش‌هایی که موتور می‌خواند یا می‌نویسد عوض شود، هش عوض می‌شود.
    /// برای فهمیدن «کهنه شدن» پیشنهاد و لازم بودن بررسی دوباره.
    /// </summary>
    public string ContentHash => PersianText.Hash(string.Join('\u001F',
        Name, Slug, SeoTitle, MetaDescription, FocusKeyword, FaqJson, NoIndex, BodyHtml, ShortText,
        string.Join(',', Images.Select(i => i.Alt))));

    /// <summary>
    /// کپی با مقدارهای پیشنهادی (برای پیش‌بینی نمرهٔ بعد از اعمال). فقط بخش‌های fields جایگزین می‌شوند.
    /// </summary>
    public SeoSubject With(SeoProposal proposal, SeoFields fields)
    {
        var images = Images;
        if (fields.HasFlag(SeoFields.ImageAlt) && proposal.ImageAlts.Count > 0)
        {
            images = Images.Select((image, index) =>
            {
                var alt = proposal.ImageAlts.FirstOrDefault(a => image.Id.HasValue ? a.ImageId == image.Id : a.Index == index);
                return alt is null ? image : image with { Alt = alt.Alt };
            }).ToList();
        }

        return new SeoSubject
        {
            Kind = Kind, EntityId = EntityId, Name = Name, EnglishName = EnglishName,
            Slug = fields.HasFlag(SeoFields.Slug) && !string.IsNullOrWhiteSpace(proposal.Slug) ? proposal.Slug! : Slug,
            PathPrefix = PathPrefix,
            SeoTitle = fields.HasFlag(SeoFields.Title) && proposal.Title is not null ? proposal.Title : SeoTitle,
            MetaDescription = fields.HasFlag(SeoFields.MetaDescription) && proposal.MetaDescription is not null ? proposal.MetaDescription : MetaDescription,
            FocusKeyword = fields.HasFlag(SeoFields.FocusKeyword) && proposal.FocusKeyword is not null ? proposal.FocusKeyword : FocusKeyword,
            FaqJson = fields.HasFlag(SeoFields.Faq) && proposal.Faq.Count > 0 ? SeoFaq.Serialize(proposal.Faq) : FaqJson,
            NoIndex = NoIndex, HasCanonicalElsewhere = HasCanonicalElsewhere,
            DefaultTitle = DefaultTitle, DefaultDescription = DefaultDescription,
            BodyHtml = fields.HasFlag(SeoFields.Description) && proposal.DescriptionHtml is not null ? proposal.DescriptionHtml : BodyHtml,
            ShortText = ShortText, Images = images, Views = Views, CreatedAt = CreatedAt,
            Product = Product, Listing = Listing, RelatedLinks = RelatedLinks,
        };
    }
}

/// <summary>متن جایگزین پیشنهادی یک تصویر. ImageId برای تصویر محصول؛ Index برای تصویر شاخص مقاله (۰).</summary>
public sealed record SeoImageAlt(Guid? ImageId, int Index, string Alt);

/// <summary>
/// مقدارهای یک پیشنهاد. فقط بخش‌های پرشده پیشنهاد شده‌اند (null = این بخش تغییری ندارد).
/// مثال JSON: {"Title":"قیمت و خرید هدفون سونی WH-1000XM5","FocusKeyword":"هدفون سونی wh-1000xm5", ...}
/// </summary>
public sealed class SeoProposal
{
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? FocusKeyword { get; set; }
    public string? Slug { get; set; }
    public string? DescriptionHtml { get; set; }
    public List<SeoFaqItem> Faq { get; set; } = [];
    public List<SeoImageAlt> ImageAlts { get; set; } = [];

    /// <summary>بخش‌هایی که واقعاً مقدار دارند.</summary>
    [JsonIgnore]
    public SeoFields Fields =>
        (Title is not null ? SeoFields.Title : 0)
        | (MetaDescription is not null ? SeoFields.MetaDescription : 0)
        | (FocusKeyword is not null ? SeoFields.FocusKeyword : 0)
        | (Slug is not null ? SeoFields.Slug : 0)
        | (DescriptionHtml is not null ? SeoFields.Description : 0)
        | (Faq.Count > 0 ? SeoFields.Faq : 0)
        | (ImageAlts.Count > 0 ? SeoFields.ImageAlt : 0);

    public string ToJson() => JsonSerializer.Serialize(this, SeoJson.Options);

    public static SeoProposal FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SeoProposal();
        try { return JsonSerializer.Deserialize<SeoProposal>(json, SeoJson.Options) ?? new SeoProposal(); }
        catch (JsonException) { return new SeoProposal(); }
    }
}

/// <summary>تنظیمات JSON موتور (فارسی بدون escape تا در دیتابیس خوانا باشد).</summary>
public static class SeoJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return default; }
    }
}

/// <summary>خواندن/نوشتن امن پرسش‌وپاسخ (ستون FaqJson). در صفحه‌های عمومی هم استفاده می‌شود.</summary>
public static class SeoFaq
{
    /// <summary>حداکثر تعداد پرسش در هر صفحه (بیشتر از این خواننده را خسته می‌کند).</summary>
    public const int MaxItems = 8;

    public static IReadOnlyList<SeoFaqItem> Parse(string? json)
    {
        var items = SeoJson.Deserialize<List<SeoFaqItem>>(json);
        if (items is null)
            return [];
        return items
            .Where(i => !string.IsNullOrWhiteSpace(i?.Question) && !string.IsNullOrWhiteSpace(i?.Answer))
            .Take(MaxItems)
            .ToList();
    }

    public static string Serialize(IEnumerable<SeoFaqItem> items) => SeoJson.Serialize(items.Take(MaxItems).ToList());
}

/// <summary>
/// نمای کل سایت برای بررسی‌های بین‌صفحه‌ای. SeoEngineService یک بار در هر اجرا می‌سازد.
/// کلیدها نرمال‌شده‌اند؛ مقدار = شناسهٔ صفحه‌هایی که آن عنوان/توضیحات/کلمه را دارند.
/// </summary>
public sealed class SeoSiteIndex
{
    public Dictionary<string, List<Guid>> Titles { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Guid>> Descriptions { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<Guid>> FocusKeywords { get; } = new(StringComparer.Ordinal);

    /// <summary>اثر انگشت متن هر صفحه برای پیدا کردن متن‌های تقریباً تکراری (مثلاً توضیحات کپی‌شده از تأمین‌کننده).</summary>
    public Dictionary<Guid, ulong> BodyFingerprints { get; } = [];

    /// <summary>آدرس‌های موجود هر نوع صفحه (برای یکتا بودن نامک پیشنهادی). کلید: «/product/slug».</summary>
    public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// حافظهٔ الگوهای جمله در کل سایت (مرحلهٔ یازدهم). نویسنده با آن الگوهای پرتکرار را کنار می‌گذارد تا صفحه‌های
    /// مشابه متن قالبی یکسان نگیرند.
    /// </summary>
    public PhraseUsage Usage { get; } = new();

    /// <summary>آمار مشخصات محصولات هر دسته (کلید: نامک دسته یا نام آن).</summary>
    public Dictionary<string, CategoryProfile> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>آمار یک دسته. null = دسته‌ای با این کلید ثبت نشده.</summary>
    public CategoryProfile? Category(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Categories.TryGetValue(key, out var profile) ? profile : null;

    public static SeoSiteIndex Build(IEnumerable<SeoSubject> subjects)
    {
        var index = new SeoSiteIndex();
        foreach (var subject in subjects)
            index.Add(subject);
        return index;
    }

    public void Add(SeoSubject subject)
    {
        AddTo(Titles, PersianText.Normalize(subject.EffectiveTitle), subject.EntityId);
        AddTo(Descriptions, PersianText.Normalize(subject.EffectiveDescription), subject.EntityId);
        AddTo(FocusKeywords, PersianText.Normalize(subject.FocusKeyword), subject.EntityId);
        Paths.Add(subject.Path);
        var text = PersianText.HtmlToText(subject.BodyHtml);
        if (PersianText.WordCount(text) >= 60)
            BodyFingerprints[subject.EntityId] = TextModels.SimHash(text);

        // الگوهای جمله‌های این صفحه (عنوان، توضیحات، متن، پرسش‌وپاسخ) برای سنجش «قالبی بودن».
        var faq = subject.Faq.Select(f => f.Question + "\n" + f.Answer);
        Usage.Register(subject.EntityId, subject.Kind,
            new[] { subject.EffectiveTitle, subject.EffectiveDescription, text }.Concat(faq), PhraseUsage.TermsOf(subject));

        if (subject.Product is { } product && (product.CategorySlug ?? product.CategoryName) is { Length: > 0 } category)
        {
            if (!Categories.TryGetValue(category, out var profile))
                Categories[category] = profile = new CategoryProfile();
            profile.Add(subject.EntityId, product.Features);
        }
    }

    /// <summary>بعد از اعمال یک پیشنهاد، مقدارهای قدیمی صفحه را بردار و جدیدها را بگذار.</summary>
    public void Replace(SeoSubject before, SeoSubject after)
    {
        RemoveFrom(Titles, PersianText.Normalize(before.EffectiveTitle), before.EntityId);
        RemoveFrom(Descriptions, PersianText.Normalize(before.EffectiveDescription), before.EntityId);
        RemoveFrom(FocusKeywords, PersianText.Normalize(before.FocusKeyword), before.EntityId);
        Paths.Remove(before.Path);
        BodyFingerprints.Remove(before.EntityId);
        Usage.Unregister(before.EntityId);
        if (Category(before.Product?.CategorySlug ?? before.Product?.CategoryName) is { } profile)
            profile.Remove(before.EntityId);
        Add(after);
    }

    /// <summary>تعداد صفحه‌های «دیگر» با همین کلید.</summary>
    public static int Others(Dictionary<string, List<Guid>> map, string? key, Guid self)
    {
        var normalized = PersianText.Normalize(key);
        return normalized.Length > 0 && map.TryGetValue(normalized, out var ids) ? ids.Count(id => id != self) : 0;
    }

    private static void AddTo(Dictionary<string, List<Guid>> map, string key, Guid id)
    {
        if (key.Length == 0) return;
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        if (!list.Contains(id))
            list.Add(id);
    }

    private static void RemoveFrom(Dictionary<string, List<Guid>> map, string key, Guid id)
    {
        if (key.Length > 0 && map.TryGetValue(key, out var list))
            list.Remove(id);
    }
}

/// <summary>مرزهای استاندارد سئو (مستند در راهنمای پنل). همه بر حسب نویسه/واژه.</summary>
public static class SeoLimits
{
    public const int TitleMin = 30;
    public const int TitleMax = 60;
    public const int MetaMin = 120;
    public const int MetaMax = 160;
    public const int SlugMaxLength = 75;

    /// <summary>حداقل واژهٔ متن «خوب» و «قابل قبول» برای هر نوع صفحه.</summary>
    public static (int Good, int Ok) ContentWords(SeoEntityKind kind) => kind switch
    {
        SeoEntityKind.Product => (300, 150),
        SeoEntityKind.BlogPost => (800, 400),
        SeoEntityKind.Category => (250, 120),
        SeoEntityKind.Brand => (200, 100),
        _ => (300, 120),
    };

    /// <summary>متن کمتر از این تعداد واژه «کم‌محتوا» است و موتور مجاز است متن تکمیلی بنویسد.</summary>
    public const int ThinContentWords = 150;

    /// <summary>متن انسانی با این تعداد واژه یا بیشتر هرگز بازنویسی نمی‌شود.</summary>
    public const int ProtectedContentWords = 250;
}
