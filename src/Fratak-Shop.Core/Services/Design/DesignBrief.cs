using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.Services.Design;

/// <summary>نوع بنر خواسته‌شده در «ایده ← طرح».</summary>
public enum BriefKind
{
    General = 0,
    Category = 1,
    Brand = 2,
    Occasion = 3,
    Deal = 4,
    Club = 5,
    GiftCard = 6,
    Blog = 7,
    Campaign = 8,
    Product = 9,
}

/// <summary>دسته برای تطبیق متن ایده (همهٔ دسته‌های فعال؛ InStock = تعداد کالای موجود).</summary>
public sealed record BriefCategory(Guid Id, string Name, string Slug, int InStock)
{
    /// <summary>دور ۲۶: نام فایل تصویر فعلی دسته (برای چیدمان «عکس تمام‌قاب» اگر عکس واقعی/فضاسازی باشد).</summary>
    public string? ImageName { get; init; }
}

/// <summary>دور ۲۶: یک گزینهٔ فهرست «مقصد بنر». مثال: («دسته‌ها»، «cat:9f…»، «هدفون»).</summary>
public sealed record BriefTargetOption(string Group, string Value, string Title);

/// <summary>دور ۲۶: مقصد لینک بنر که مدیر انتخاب کرده است.</summary>
public enum BriefTargetKind
{
    /// <summary>خودکار از متن ایده (رفتار قبلی).</summary>
    Auto = 0,
    /// <summary>بدون لینک (بنر اطلاع‌رسانی؛ دکمه هم ندارد).</summary>
    None = 1,
    /// <summary>یک دستهٔ مشخص.</summary>
    Category = 2,
    /// <summary>کالای انتخاب‌شده از فهرست.</summary>
    Product = 3,
    /// <summary>یک صفحه/بخش داخلی سایت (حراج‌ها، قوانین، کمپین، باشگاه…).</summary>
    Page = 4,
}

/// <summary>
/// دور ۲۶: مقصد بنر. مثال: Parse("cat:9f…") ← (Category، Id)؛ Parse("url:/club") ← (Page، Url=/club)؛ Parse("none") ← بدون لینک.
/// مقدار ناشناخته/ناامن ← Auto (هرگز خطا نمی‌دهد). آدرس فقط داخلی (با «/» شروع شود؛ SafeLink).
/// </summary>
public sealed record BriefTarget(BriefTargetKind Kind, Guid? Id = null, string? Url = null, string? Title = null)
{
    /// <summary>مقصد پیش‌فرض (خودکار).</summary>
    public static readonly BriefTarget Auto = new(BriefTargetKind.Auto);

    /// <summary>
    /// خواندن مقدار فرم. <paramref name="value"/>: auto | none | product | cat:{guid} | url:{مسیر داخلی}؛ <paramref name="customUrl"/> = آدرس دلخواه
    /// (اگر پر باشد بر انتخاب فهرست مقدم است). <paramref name="title"/> = عنوان صفحهٔ انتخابی (برای نوشتن متن).
    /// </summary>
    public static BriefTarget Parse(string? value, string? customUrl = null, string? title = null)
    {
        if (DesignService.SafeLink(customUrl?.Trim()) is { } custom && custom.StartsWith('/'))
            return new BriefTarget(BriefTargetKind.Page, Url: custom, Title: Clean(title));
        value = value?.Trim() ?? "";
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
            return new BriefTarget(BriefTargetKind.None);
        if (value.Equals("product", StringComparison.OrdinalIgnoreCase))
            return new BriefTarget(BriefTargetKind.Product);
        if (value.StartsWith("cat:", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(value[4..], out var id) && id != Guid.Empty)
            return new BriefTarget(BriefTargetKind.Category, id);
        if (value.StartsWith("url:", StringComparison.OrdinalIgnoreCase) && DesignService.SafeLink(value[4..]) is { } url && url.StartsWith('/'))
            return new BriefTarget(BriefTargetKind.Page, Url: url, Title: Clean(title));
        return Auto;
    }

    private static string? Clean(string? title)
    {
        var t = title?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length > 60 ? t[..60] : t;
    }
}

/// <summary>برند برای تطبیق متن ایده.</summary>
public sealed record BriefBrand(Guid Id, string Name, string? EnglishName, string Slug, int InStock);

/// <summary>
/// خواستهٔ خوانده‌شده از متن کوتاه. مثال: «تخفیف هدفون یلدا ۲۰٪» ← Kind=Occasion، Occasion=yalda، Percent=20، Deal=true.
/// </summary>
public sealed record BriefIntent(BriefKind Kind, string? Occasion, int? Percent, bool Deal, string Normalized)
{
    /// <summary>دور ۲۴: ضریب امتیاز خواسته‌شده (مثلاً «امتیاز دوبرابر» ← ۲)؛ عدد خام، سقف را <see cref="DesignBrief.Offer"/> اعمال می‌کند.</summary>
    public int? PointsMultiplier { get; init; }

    /// <summary>دور ۲۴: کالایی که مدیر از فهرست «انتخاب کالا» برگزیده (بر تطبیق متن مقدم است).</summary>
    public Guid? ProductId { get; init; }

    /// <summary>دور ۲۶: مقصد انتخابی مدیر (پیش‌فرض خودکار).</summary>
    public BriefTarget Target { get; init; } = BriefTarget.Auto;
}

/// <summary>
/// دور ۲۴: «قاعدهٔ واقعی» که باید پشت بنر ساخته شود (پیش‌نویس، با انتشار بنر فعال می‌شود).
/// مثال: «۲۰٪ تخفیف هدفون با امتیاز دوبرابر» ← Percent=20، Points=2، Scope=Category(هدفون)، Code=«CMP7K2QX».
/// <para>ExistingDiscount: تخفیف واقعی فعلی کالای مشخص (فقط دامنهٔ Product)؛ کوپن طوری حساب می‌شود که جمع دو تخفیف دست‌کم Percent شود.</para>
/// </summary>
public sealed record BriefOffer(int? Percent, int? Points, CampaignOfferScope Scope, Guid? ScopeId, string? ScopeName, string? Code = null, int ExistingDiscount = 0);

/// <summary>دادهٔ واقعی مرتبط با ایده (از پایگاه داده؛ DesignService.BriefDataAsync).</summary>
public sealed record BriefData(IReadOnlyList<IdeaProduct> Products, BriefCategory? Category, BriefBrand? Brand, string SiteName);

/// <summary>
/// برنامهٔ کامل بنر: تیتر، متن، نشان (فقط تخفیف واقعی)، دکمه، لینک، کالای اصلی و هشدارها (مثال: «۵۰٪ گفته بودید ولی بیشترین تخفیف واقعی ۲۰٪ است»).
/// </summary>
public sealed record BriefPlan(BriefKind Kind, string Title, string Headline, string? Subline, string? Badge, string ButtonText, string LinkUrl,
    IdeaProduct? Hero, IReadOnlyList<IdeaProduct> Products, string Palette, IReadOnlyList<string> Warnings, string? Occasion, DateTimeOffset? ExpiresAt)
{
    /// <summary>دور ۲۴: بیشترین تخفیف واقعی کالاهای موجودِ دامنه (برای تصمیم «قاعدهٔ تازه لازم است یا نه»).</summary>
    public int MaxDiscount { get; init; }

    /// <summary>دور ۲۴: قاعدهٔ پیش‌نویسی که این برنامه با آن ساخته شد (null = فقط دادهٔ واقعی فعلی).</summary>
    public BriefOffer? Offer { get; init; }

    /// <summary>دور ۲۶: تیترهای جایگزین برای نسخه‌های ۲ و ۳ (متن خود ایده، یا جملهٔ دیگری با همان پیام) تا سه نسخه فقط در رنگ فرق نکنند.</summary>
    public IReadOnlyList<string> AltHeadlines { get; init; } = [];
}

/// <summary>
/// «ایده ← طرح» بدون هوش مصنوعی (دور ۲۳): مدیر یک جملهٔ کوتاه می‌نویسد (مثل «حراج هدفون برای یلدا» یا «کارت هدیه»)
/// و این کلاس از دادهٔ واقعی فروشگاه یک بنر کامل می‌سازد: تیتر، متن، کالاها، عکس، رنگ، لینک و نسخهٔ موبایل – در ۳ نسخهٔ متفاوت.
/// <list type="bullet">
/// <item>انواع: دسته، برند، مناسبت (یلدا، نوروز، مدارس، بلک‌فرایدی، روز مادر/پدر، ولنتاین، دانشجو، معلم، سپندارمذگان)،
/// حراج، باشگاه مشتریان، کارت هدیه، بلاگ، کمپین و کالای مشخص.</item>
/// <item>صداقت: درصد تخفیف از دادهٔ واقعی؛ اگر عدد گفته‌شده بیشتر از تخفیف واقعی باشد، عدد واقعی می‌آید و هشدار داده می‌شود.
/// شمارش معکوس جعلی و «فقط امروز» ساخته نمی‌شود.</item>
/// <item>هیچ چیز خودکار منتشر نمی‌شود: ۳ طرح «پیشنهادی» ساخته می‌شود و مدیر یکی (یا چند تا برای A/B) را منتشر می‌کند.</item>
/// </list>
/// <para>محل استفاده: Web/Infrastructure/Design/DesignStudio.BuildFromBriefAsync ← صفحهٔ «استودیوی طراحی» (فرم «ایده ← طرح»).</para>
/// </summary>
public static class DesignBrief
{
    /// <summary>بیشترین طول متن ایده.</summary>
    public const int MaxLength = 200;

    private static readonly (string Key, string Name, string Palette, string[] Words, string Headline, string Subline)[] Occasions =
    [
        ("yalda", "شب یلدا", "festive", ["یلدا"], "جشنوارهٔ یلدا", "بلندترین شب سال، با خریدی به‌یادماندنی"),
        ("nowruz", "نوروز", "forest", ["نوروز", "عید نوروز", "سال نو", "عیدی"], "جشنوارهٔ نوروزی", "سال نو، وسایل نو؛ پیش از تعطیلات سفارش دهید"),
        ("school", "بازگشایی مدارس", "royal", ["مدرسه", "مدارس", "بازگشایی", "دانش آموز", "مهر"], "آماده برای مهر", "هر چه برای درس و مدرسه لازم است"),
        ("blackfriday", "بلک‌فرایدی", "night", ["بلک فرایدی", "بلک‌فرایدی", "black friday", "جمعه سیاه"], "حراج بلک‌فرایدی", "تخفیف‌های پایان پاییز روی کالاهای منتخب"),
        ("mother", "روز مادر", "sunset", ["روز مادر", "مادر"], "هدیهٔ روز مادر", "هدیه‌ای که هر روز به کارش می‌آید"),
        ("father", "روز پدر", "night", ["روز پدر", "پدر", "روز مرد"], "هدیهٔ روز پدر", "انتخابی کاربردی برای پدر"),
        ("valentine", "ولنتاین", "sunset", ["ولنتاین"], "هدیهٔ ولنتاین", "هدیه‌ای برای کسی که دوستش دارید"),
        ("sepandarmazgan", "سپندارمذگان", "sunset", ["سپندارمذگان", "روز عشق"], "هدیهٔ سپندارمذگان", "روز عشق ایرانی؛ هدیه‌ای ماندگار"),
        ("student", "روز دانشجو", "royal", ["روز دانشجو", "دانشجو"], "هدیهٔ روز دانشجو", "ابزارهایی برای درس و کار"),
        ("teacher", "روز معلم", "forest", ["روز معلم", "معلم"], "هدیهٔ روز معلم", "سپاسی کوچک از معلم"),
    ];

    private static readonly Regex PercentPattern = new(@"(?<!\d)(\d{1,2})\s*(?:%|٪|درصد)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>دور ۲۴: «۲ برابر»، «x3»، «×۲» (ارقام پیش‌تر لاتین شده‌اند).</summary>
    private static readonly Regex MultiplierPattern = new(@"(?<!\d)(\d{1,2})\s*(?:برابر|x|×)|(?<![a-z])(?:x|×)\s*(\d{1,2})(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Word, int Value)[] MultiplierWords =
    [
        ("دوبرابر", 2), ("دو برابر", 2), ("دوبل", 2), ("سه برابر", 3), ("سهبرابر", 3), ("چهار برابر", 4), ("پنج برابر", 5),
    ];

    /// <summary>دور ۲۴: بیشترین درصد تخفیف کمپین (بیشتر از این ← همین عدد با هشدار).</summary>
    public const int MaxOfferPercent = 50;

    /// <summary>دور ۲۴: بیشترین ضریب امتیاز کمپین.</summary>
    public const int MaxPointsMultiplier = 5;

    /// <summary>
    /// دور ۲۴: ضریب امتیاز خواسته‌شده در متن (فقط وقتی «امتیاز» آمده باشد). مثال‌ها: «امتیاز دوبرابر» ← ۲، «۳ برابر امتیاز» ← ۳، «امتیاز x4» ← ۴،
    /// «امتیاز ۱۰ برابر» ← ۱۰ (سقف بعداً)، «هدفون x2» ← null (بدون واژهٔ امتیاز).
    /// </summary>
    public static int? ParseMultiplier(string normalized)
    {
        if (!ContainsAny(normalized, "امتیاز", "point"))
            return null;
        foreach (var (word, value) in MultiplierWords)
        {
            if (normalized.Contains(word, StringComparison.Ordinal))
                return value;
        }
        var match = MultiplierPattern.Match(normalized);
        if (!match.Success)
            return null;
        var digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return int.TryParse(digits, CultureInfo.InvariantCulture, out var n) && n >= 2 ? n : null;
    }

    /// <summary>
    /// یکسان‌سازی متن برای تطبیق: ی/ک عربی ← فارسی، ارقام فارسی/عربی ← لاتین، نیم‌فاصله ← فاصله، حروف کوچک، فاصلهٔ تکراری حذف.
    /// مثال: «هدفون‌ها ۲۰٪» ← «هدفون ها 20٪».
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
        {
            builder.Append(c switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                '\u200c' or '\u200f' or '\u200e' or '\u00a0' => ' ',
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                _ => char.ToLowerInvariant(c),
            });
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>
    /// خواندن نوع ایده از متن. ترتیب اهمیت: کارت هدیه، باشگاه، بلاگ، مناسبت، حراج، کمپین، بقیه (دسته/برند/کالا را بعداً داده تعیین می‌کند).
    /// مثال: «کارت هدیه یلدا» ← GiftCard (با مناسبت یلدا برای رنگ)؛ «حراج لپ‌تاپ» ← Deal.
    /// </summary>
    public static BriefIntent Parse(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length > MaxLength)
            normalized = normalized[..MaxLength];
        int? percent = PercentPattern.Match(normalized) is { Success: true } m && int.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out var p) && p is > 0 and < 100
            ? p
            : null;
        var deal = percent is not null || ContainsAny(normalized, "تخفیف", "حراج", "ارزان", "شگفت", "آف ", "sale", "off");
        var occasion = Occasions.FirstOrDefault(o => o.Words.Any(w => ContainsWord(normalized, Normalize(w)))).Key;
        var multiplier = ParseMultiplier(normalized);

        BriefKind kind;
        if (ContainsAny(normalized, "کارت هدیه", "گیفت کارت", "گیفت‌کارت", "gift card", "giftcard"))
            kind = BriefKind.GiftCard;
        // دور ۲۴: «امتیاز دوبرابر» یک کمپین خرید است نه معرفی باشگاه؛ فقط «باشگاه» یا «امتیاز» بدون ضریب ← بنر باشگاه.
        else if (ContainsAny(normalized, "باشگاه", "club") || (multiplier is null && ContainsAny(normalized, "امتیاز")))
            kind = BriefKind.Club;
        else if (ContainsAny(normalized, "مقاله", "بلاگ", "وبلاگ", "راهنمای خرید", "blog"))
            kind = BriefKind.Blog;
        else if (occasion is not null)
            kind = BriefKind.Occasion;
        else if (deal)
            kind = BriefKind.Deal;
        else if (ContainsAny(normalized, "کمپین", "جشنواره", "campaign"))
            kind = BriefKind.Campaign;
        else
            kind = BriefKind.General;
        return new BriefIntent(kind, occasion, percent, deal, normalized) { PointsMultiplier = multiplier };
    }

    /// <summary>
    /// دور ۲۴: آیا برای راست بودن ادعای بنر یک «قاعدهٔ واقعی» تازه لازم است؟ (تابع خالص؛ کد را سازنده می‌دهد.)
    /// <list type="bullet">
    /// <item>درصد گفته‌شده بیشتر از بیشترین تخفیف واقعی دامنه ← کد تخفیف تازه با همان درصد (سقف ۵۰٪، با هشدار).
    /// اگر تخفیف واقعی فعلی کافی باشد قاعدهٔ تازه ساخته نمی‌شود (تخفیف روی تخفیف = ضرر).</item>
    /// <item>ضریب امتیاز (۲ تا ۵ برابر) ← قاعدهٔ امتیاز برای خریدهای همان دامنه.</item>
    /// <item>کارت هدیه و بلاگ قاعده ندارند؛ بنر باشگاه فقط ضریب امتیاز می‌گیرد.</item>
    /// <item>دامنه: کالای مشخص ← Product؛ وگرنه دسته، برند، و در نبود هر دو کل فروشگاه.</item>
    /// </list>
    /// مثال: «۳۰٪ تخفیف هدفون» با بیشترین تخفیف واقعی ۱۰٪ ← (30، null، Category، id هدفون، «هدفون»).
    /// </summary>
    public static (BriefOffer? Offer, IReadOnlyList<string> Warnings) Offer(BriefIntent intent, BriefPlan plan, BriefData data)
    {
        var warnings = new List<string>();
        if (plan.Kind is BriefKind.GiftCard or BriefKind.Blog)
            return (null, warnings);
        int? percent = null;
        if (plan.Kind != BriefKind.Club && intent.Percent is { } asked && asked > plan.MaxDiscount)
        {
            percent = Math.Min(asked, MaxOfferPercent);
            if (asked > MaxOfferPercent)
                warnings.Add($"بیشترین تخفیف کمپین {DesignTemplates.FaDigits(MaxOfferPercent + "%")} است؛ همین عدد گذاشته شد.");
        }
        int? points = null;
        if (intent.PointsMultiplier is { } wanted && wanted >= 2)
        {
            points = Math.Min(wanted, MaxPointsMultiplier);
            if (wanted > MaxPointsMultiplier)
                warnings.Add($"بیشترین ضریب امتیاز {DesignTemplates.FaDigits(MaxPointsMultiplier.ToString(CultureInfo.InvariantCulture))} برابر است؛ همین عدد گذاشته شد.");
        }
        if (percent is null && points is null)
            return (null, warnings);

        BriefOffer offer;
        if (plan.Hero is { } hero && plan.Kind is BriefKind.Product or BriefKind.Deal && plan.LinkUrl.StartsWith("/product/", StringComparison.Ordinal))
            offer = new BriefOffer(percent, points, CampaignOfferScope.Product, hero.Id, hero.Name, ExistingDiscount: hero.DiscountPercent);
        else if (data.Category is { } category)
            offer = new BriefOffer(percent, points, CampaignOfferScope.Category, category.Id, category.Name);
        else if (data.Brand is { } brand)
            offer = new BriefOffer(percent, points, CampaignOfferScope.Brand, brand.Id, brand.Name);
        else
            offer = new BriefOffer(percent, points, CampaignOfferScope.All, null, null);
        return (offer, warnings);
    }

    /// <summary>
    /// دستهٔ اشاره‌شده در متن (طولانی‌ترین نام منطبق؛ بعد بیشترین کالای موجود). مثال: «حراج هدفون بی سیم» و دسته‌های «هدفون»، «هدفون بی‌سیم»
    /// ← «هدفون بی‌سیم». نامی که کمتر از ۲ حرف دارد تطبیق داده نمی‌شود.
    /// </summary>
    public static BriefCategory? MatchCategory(string text, IEnumerable<BriefCategory> categories)
    {
        var normalized = Normalize(text);
        return categories
            .Select(c => (Category: c, Name: Normalize(c.Name)))
            .Where(x => x.Name.Length >= 2 && (ContainsWord(normalized, x.Name) || ContainsWord(normalized, Plural(x.Name))))
            .OrderByDescending(x => x.Name.Length).ThenByDescending(x => x.Category.InStock)
            .Select(x => x.Category)
            .FirstOrDefault();
    }

    /// <summary>برند اشاره‌شده (نام فارسی یا انگلیسی). مثال: «محصولات سامسونگ» یا «samsung» ← برند سامسونگ.</summary>
    public static BriefBrand? MatchBrand(string text, IEnumerable<BriefBrand> brands)
    {
        var normalized = Normalize(text);
        return brands
            .Where(b => (Normalize(b.Name) is { Length: >= 2 } fa && ContainsWord(normalized, fa))
                        || (Normalize(b.EnglishName) is { Length: >= 2 } en && ContainsWord(normalized, en)))
            .OrderByDescending(b => b.Name.Length).ThenByDescending(b => b.InStock)
            .FirstOrDefault();
    }

    /// <summary>
    /// ساخت برنامهٔ بنر از خواسته و دادهٔ واقعی. کالاها فقط موجود و دارای عکس. خروجی همیشه یک برنامهٔ قابل ساخت است
    /// (بدون داده ← بنر عمومی فروشگاه با لینک «همهٔ کالاها»).
    /// </summary>
    /// <param name="offer">دور ۲۴: قاعدهٔ پیش‌نویس (از <see cref="Offer"/> + کد). اگر باشد، نشان و متن بنر همان قاعده را می‌گوید
    /// (مثلاً «۲۰٪ تخفیف» + «با کد CMP7K2QX») چون با انتشار بنر واقعی می‌شود.</param>
    public static BriefPlan Plan(string text, BriefIntent intent, BriefData data, DateTimeOffset now, BriefOffer? offer = null)
    {
        var warnings = new List<string>();
        var products = data.Products.Where(p => p.Stock > 0 && p.FinalPrice > 0).ToList();
        var site = string.IsNullOrWhiteSpace(data.SiteName) ? "فروشگاه" : data.SiteName.Trim();
        var occasion = intent.Occasion is null ? default : Occasions.First(o => o.Key == intent.Occasion);
        var category = data.Category;
        var brand = data.Brand;

        // کالای مشخص: متن دست‌کم ۲ کلمهٔ معنادار از نام یک کالا را دارد.
        // دور ۲۴: کالای انتخاب‌شده از فهرست بر تطبیق متن مقدم است (اگر موجود نباشد هشدار).
        IdeaProduct? product = null;
        if (intent.ProductId is { } pickedId)
        {
            product = products.FirstOrDefault(p => p.Id == pickedId);
            if (product is null)
                warnings.Add("کالای انتخاب‌شده الان موجود نیست یا عکس ندارد؛ طرح بدون آن ساخته شد.");
        }
        else if (intent.Kind is BriefKind.General or BriefKind.Deal or BriefKind.Occasion)
        {
            product = MatchProduct(intent.Normalized, products);
        }
        var kind = intent.Kind;
        if (kind == BriefKind.General)
            kind = product is not null ? BriefKind.Product : category is not null ? BriefKind.Category : brand is not null ? BriefKind.Brand : BriefKind.General;

        List<IdeaProduct> scope = product is not null && kind is BriefKind.Product or BriefKind.Deal ? new List<IdeaProduct> { product } : products;
        var maxDiscount = scope.Where(p => p.Stock >= 1).Select(p => p.DiscountPercent).DefaultIfEmpty(0).Max();
        string? badge = null;
        var offerPoints = offer?.Points is { } op ? DesignTemplates.FaDigits(op.ToString(CultureInfo.InvariantCulture)) : null;
        if (offer?.Percent is { } offerPercent)
        {
            // قاعدهٔ کمپین با انتشار بنر فعال می‌شود ← ادعا راست است.
            badge = $"{DesignTemplates.FaDigits(offerPercent + "%")} تخفیف";
        }
        else if (intent.Deal || kind == BriefKind.Deal)
        {
            if (maxDiscount >= 5)
            {
                var shown = intent.Percent is { } asked && asked <= maxDiscount ? asked : maxDiscount;
                if (intent.Percent is { } claimed && claimed > maxDiscount)
                    warnings.Add($"«{claimed}٪» نوشته بودید ولی بیشترین تخفیف واقعی کالاهای موجود {maxDiscount}٪ است؛ همان عدد واقعی روی بنر آمد.");
                badge = $"تا {DesignTemplates.FaDigits(shown + "%")} تخفیف";
            }
            else
            {
                warnings.Add("کالای موجود با تخفیف واقعی (≥۵٪) پیدا نشد؛ بنر بدون ادعای تخفیف ساخته شد. اول تخفیف را روی کالاها بگذارید.");
            }
        }

        var hero = product
                   ?? (badge is not null ? scope.OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Sales).FirstOrDefault() : null)
                   ?? scope.Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl)).OrderByDescending(p => p.Sales).ThenByDescending(p => p.Views).FirstOrDefault();
        if (hero is not null && string.IsNullOrWhiteSpace(hero.ImageUrl))
            hero = scope.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ImageUrl)) ?? hero;
        var top = scope.OrderByDescending(p => p.DiscountPercent).ThenByDescending(p => p.Sales).Take(3).ToList();
        var inStock = category?.InStock ?? brand?.InStock ?? products.Count;
        var from = scope.Count == 0 ? null : (decimal?)scope.Min(p => p.FinalPrice);
        var discountsLink = "/products?Discounted=true&Sort=MostDiscounted";

        string headline, title, button, link;
        string? subline;
        var palette = occasion.Key is null ? "ocean" : occasion.Palette;
        DateTimeOffset? expires = now.AddDays(30);
        switch (kind)
        {
            case BriefKind.GiftCard:
                title = "کارت هدیه";
                headline = $"کارت هدیهٔ {site}";
                subline = occasion.Key is null ? "هدیه‌ای که همیشه اندازه است؛ انتخاب با خودِ او" : $"هدیهٔ {occasion.Name}: انتخاب با خودِ او";
                button = "خرید کارت هدیه";
                link = "/profile/gift-cards";
                palette = occasion.Key is null ? "royal" : palette;
                hero = null;
                break;
            case BriefKind.Club:
                title = "باشگاه مشتریان";
                headline = $"باشگاه مشتریان {site}";
                subline = "با هر خرید امتیاز بگیرید و امتیاز را با جایزه عوض کنید";
                button = "عضویت و جایزه‌ها";
                link = "/club";
                palette = "gold";
                hero = null;
                break;
            case BriefKind.Blog:
                title = "راهنمای خرید";
                headline = category is null ? "پیش از خرید بخوانید" : $"راهنمای خرید {category.Name}";
                subline = category is null ? "راهنماهای خرید و مقایسه‌های کاربردی" : "نکته‌هایی که قبل از انتخاب باید بدانید";
                button = "خواندن راهنما";
                link = "/blog";
                palette = "clean";
                break;
            case BriefKind.Occasion:
                title = occasion.Name;
                headline = category is null ? occasion.Headline : $"{occasion.Headline}: {category.Name}";
                subline = badge is null ? occasion.Subline : $"{occasion.Subline}؛ تخفیف واقعی روی کالاهای موجود";
                button = "دیدن پیشنهادها";
                link = category is not null ? $"/category/{category.Slug}" : badge is not null ? discountsLink : "/products?Sort=BestSelling";
                break;
            case BriefKind.Product when product is not null:
                title = "معرفی کالا";
                headline = DesignTemplates.Shorten(product.Name, 46);
                subline = product.CategoryName is null ? null : "از دستهٔ " + product.CategoryName;
                button = badge is null ? "مشاهده و خرید" : "خرید با تخفیف";
                link = "/product/" + product.Slug;
                expires = now.AddDays(14);
                break;
            case BriefKind.Deal:
                title = "حراج";
                headline = product is not null ? DesignTemplates.Shorten(product.Name, 46)
                    : category is not null ? $"حراج {category.Name}"
                    : brand is not null ? $"حراج {brand.Name}" : "حراج ویژهٔ این هفته";
                subline = badge is null ? "قیمت‌های به‌روز کالاهای موجود" : "تخفیف واقعی روی کالاهای موجود";
                button = "خرید با تخفیف";
                link = product is not null ? "/product/" + product.Slug
                    : category is not null ? $"/category/{category.Slug}?Discounted=true"
                    : brand is not null ? $"/brand/{brand.Slug}" : discountsLink;
                palette = "sunset";
                expires = now.AddDays(7);
                break;
            case BriefKind.Category when category is not null:
                title = "معرفی دسته";
                headline = category.Name;
                subline = from is { } min
                    ? $"{DesignTemplates.FaDigits(inStock.ToString(CultureInfo.InvariantCulture))} مدل موجود، از {DesignTemplates.Toman(min)}"
                    : $"{DesignTemplates.FaDigits(inStock.ToString(CultureInfo.InvariantCulture))} مدل موجود";
                button = "دیدن همه";
                link = $"/category/{category.Slug}";
                break;
            case BriefKind.Brand when brand is not null:
                title = "معرفی برند";
                headline = $"محصولات {brand.Name}";
                subline = $"{DesignTemplates.FaDigits(brand.InStock.ToString(CultureInfo.InvariantCulture))} کالای اصل و موجود";
                button = "دیدن محصولات";
                link = $"/brand/{brand.Slug}";
                break;
            case BriefKind.Campaign:
                title = "کمپین";
                headline = category is null ? $"جشنوارهٔ خرید {site}" : $"جشنوارهٔ {category.Name}";
                subline = badge is null ? "پیشنهادهای منتخب این هفته" : "تخفیف واقعی روی کالاهای منتخب";
                button = "دیدن جشنواره";
                link = category is not null ? $"/category/{category.Slug}" : badge is not null ? discountsLink : "/products?Sort=BestSelling";
                palette = "royal";
                break;
            default:
                kind = BriefKind.General;
                title = "بنر فروشگاه";
                headline = category?.Name ?? brand?.Name ?? site;
                subline = "خرید آسان با ارسال سریع";
                button = "مشاهدهٔ محصولات";
                link = category is not null ? $"/category/{category.Slug}" : "/products";
                break;
        }

        if (hero is null && kind is not (BriefKind.Club or BriefKind.GiftCard or BriefKind.Blog))
            warnings.Add("کالای موجود با عکس برای این ایده پیدا نشد؛ طرح بدون عکس کالا ساخته شد.");

        if (offer is not null)
        {
            // دور ۲۴: متن بنر قاعدهٔ کمپین را دقیق می‌گوید (کد تخفیف و ضریب امتیاز).
            var parts = new List<string>();
            if (offer.Percent is not null && !string.IsNullOrWhiteSpace(offer.Code))
                parts.Add("با کد " + offer.Code);
            if (offerPoints is not null)
                parts.Add($"امتیاز باشگاه {offerPoints} برابر");
            if (parts.Count > 0)
                subline = string.Join("، ", parts);
            badge ??= offerPoints is null ? null : $"امتیاز {offerPoints} برابر";
            if (offer.Percent is not null && offer.Scope == CampaignOfferScope.All && link.StartsWith("/products?Discounted", StringComparison.Ordinal))
                link = "/products";
            warnings.Add(offer.Percent is not null && offerPoints is not null
                ? "تخفیف و امتیاز کمپین به‌صورت پیش‌نویس ذخیره شد و فقط با انتشار بنر فعال می‌شود."
                : offer.Percent is not null
                    ? "کد تخفیف کمپین به‌صورت پیش‌نویس ذخیره شد و فقط با انتشار بنر فعال می‌شود."
                    : "ضریب امتیاز کمپین به‌صورت پیش‌نویس ذخیره شد و فقط با انتشار بنر فعال می‌شود.");
        }

        // دور ۲۶: مقصد انتخابی مدیر بر لینک خودکار مقدم است.
        var target = intent.Target;
        var ideaHeadline = IdeaHeadline(text, intent, maxDiscount, offer);
        switch (target.Kind)
        {
            case BriefTargetKind.None:
                // بنر اطلاع‌رسانی: بدون لینک و بدون دکمه (دکمه‌ای که جایی نمی‌رود گمراه‌کننده است).
                link = "";
                button = "";
                if (kind == BriefKind.General && ideaHeadline is not null)
                    headline = ideaHeadline;
                break;
            case BriefTargetKind.Page when target.Url is not null:
                link = target.Url;
                if (kind == BriefKind.General)
                {
                    title = target.Title ?? title;
                    headline = ideaHeadline ?? target.Title ?? headline;
                    subline = target.Title is not null && headline != target.Title ? target.Title : subline;
                    button = "مشاهده";
                }
                break;
        }

        var alternatives = new List<string>();
        if (ideaHeadline is not null)
            alternatives.Add(ideaHeadline);
        if (category is not null && kind is BriefKind.Deal or BriefKind.Category or BriefKind.Occasion or BriefKind.Campaign)
            alternatives.Add(badge is null || badge.StartsWith("امتیاز", StringComparison.Ordinal) ? $"خرید {category.Name}"
                : badge.StartsWith("تا ", StringComparison.Ordinal) ? $"{category.Name}، {badge}" : $"{category.Name} با {badge}");
        if (product is not null && kind is BriefKind.Product or BriefKind.Deal)
            alternatives.Add(badge is not null && !badge.StartsWith("امتیاز", StringComparison.Ordinal) ? $"{badge} روی {DesignTemplates.Shorten(product.Name, 30)}" : DesignTemplates.Shorten(product.Name, 30));
        if (brand is not null && kind is BriefKind.Brand or BriefKind.Deal)
            alternatives.Add($"دنیای {brand.Name}");
        var shortHeadline = DesignTemplates.Shorten(headline, 60);
        var alts = alternatives.Select(a => DesignTemplates.Shorten(a, 60)).Where(a => a.Length >= 3 && a != shortHeadline).Distinct(StringComparer.Ordinal).Take(2).ToList();

        var shortTitle = DesignTemplates.Shorten(string.IsNullOrWhiteSpace(text) ? title : text.Trim(), 120);
        var safeLink = target.Kind == BriefTargetKind.None ? "" : DesignService.SafeLink(link) ?? "/products";
        return new BriefPlan(kind, shortTitle, shortHeadline, subline is null ? null : DesignTemplates.Shorten(subline, 90), badge, button,
            safeLink, hero, top, palette, warnings, occasion.Key, expires)
        {
            MaxDiscount = maxDiscount,
            Offer = offer,
            AltHeadlines = alts,
        };
    }

    private static readonly Regex IdeaNoise = new(
        @"^(?:لطفا\s+|یه\s+|یک\s+)?(?:بنر|طرح|تبلیغ)(?:\s+(?:برای|درباره|دربارهٔ|با موضوع))?\s+|\s+(?:بساز|بسازید|درست کن|طراحی کن|بزن)\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// دور ۲۶: خود متن ایده به‌عنوان تیتر (وقتی تیتر خوبی است). «بنر»/«بساز» حذف، ارقام فارسی، حداکثر ۴۶ نویسه.
    /// صداقت: اگر درصد متن از تخفیف واقعی (یا قاعدهٔ کمپین) بیشتر باشد ← null (عدد غیرواقعی روی بنر نمی‌رود).
    /// مثال: «بنر خرید محصولات خوراکی با ۲۰٪ تخفیف» (تخفیف واقعی ۲۵٪) ← «خرید محصولات خوراکی با ۲۰٪ تخفیف».
    /// </summary>
    public static string? IdeaHeadline(string? text, BriefIntent intent, int maxDiscount, BriefOffer? offer)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (intent.Percent is { } asked && asked > maxDiscount && offer?.Percent != asked)
            return null;
        if (intent.PointsMultiplier is { } multiplier && offer?.Points != multiplier)
            return null;
        var cleaned = Regex.Replace(IdeaNoise.Replace(text.Trim(), "").Trim(), @"\s+", " ");
        cleaned = DesignTemplates.FaDigits(cleaned).Replace("%", "٪", StringComparison.Ordinal);
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || words.Length > 9 || cleaned.Length > 46 || words.All(w => StopWords.Contains(Normalize(w))))
            return null;
        return cleaned;
    }

    /// <summary>
    /// سه نسخهٔ متفاوت از یک برنامه (چیدمان، رنگ و تیتر متفاوت). <paramref name="imageColor"/> = رنگ غالب عکس کالا (اختیاری)؛
    /// اگر بود، نسخهٔ اول با «رنگ عکس کالا» ساخته می‌شود (پالت خوانا با کنتراست ≥ ۴٫۵).
    /// کلید هر نسخه «brief:{کالا یا -}:{هش}:{۱..۳}» است تا نسخه‌های منتشرشده خودکار گروه A/B شوند.
    /// <para>دور ۲۶ – چیدمان بر اساس محتوا:</para>
    /// <list type="bullet">
    /// <item>عکس واقعی دسته (<paramref name="photo"/>) ← «photo» (عکس تمام‌قاب) + collage/pattern.</item>
    /// <item>چند کالا (دسته/کمپین؛ <paramref name="gallery"/> ≥ ۲) ← collage، split/sale، pattern.</item>
    /// <item>یک کالا ← split یا sale، festive (مناسبت)/pattern، minimal.</item>
    /// <item>بدون عکس (باشگاه، کارت هدیه، صفحه، اطلاع‌رسانی) ← pattern، center، festive/minimal.</item>
    /// </list>
    /// </summary>
    public static List<DesignIdea> Variants(BriefPlan plan, string? imageColor, DesignImage? image, string? salt = null,
        IReadOnlyList<DesignImage>? gallery = null, DesignImage? photo = null)
    {
        // salt (مثلاً زمان ساخت) ← ساخت دوبارهٔ همان ایده بعد از رد شدن، کلید تازه می‌گیرد.
        var hash = Hash(plan.Title + "|" + plan.Headline + "|" + plan.LinkUrl + "|" + salt);
        var productKey = plan.Hero?.Id.ToString("N") ?? "-";
        var single = plan.Hero is not null && plan.Kind is BriefKind.Product or BriefKind.Deal && plan.LinkUrl.StartsWith("/product/", StringComparison.Ordinal);
        var price = single ? DesignTemplates.Toman(plan.Hero!.FinalPrice) : null;
        var oldPrice = single && plan.Hero!.DiscountPercent > 0 ? DesignTemplates.Toman(plan.Hero.Price) : null;
        var pictures = (gallery ?? Array.Empty<DesignImage>()).Where(g => g.Width > 0 && g.Height > 0).Take(3).ToList();

        var palettes = new List<string>();
        if (DesignColors.TryParse(imageColor, out _))
            palettes.Add(imageColor!.Trim().ToLowerInvariant());
        palettes.Add(plan.Palette);
        var extra = plan.Kind switch
        {
            BriefKind.Deal => new[] { "coral", "night", "candy", "sunset" },
            BriefKind.Club => new[] { "espresso", "indigo", "gold" },
            BriefKind.Occasion => new[] { "festive", "indigo", "coral" },
            BriefKind.Category => new[] { "mint", "sky", "indigo" },
            _ => new[] { "indigo", "mint", "sky", "ocean" },
        };
        foreach (var fallback in extra.Concat(new[] { "ocean", "night", "forest", "royal" }))
        {
            if (!palettes.Contains(fallback))
                palettes.Add(fallback);
        }

        string[] layouts;
        if (photo is not null && !single)
            layouts = ["photo", pictures.Count >= 2 ? "collage" : plan.Badge is not null ? "sale" : "split", "pattern"];
        else if (!single && pictures.Count >= 2)
            layouts = ["collage", plan.Badge is not null ? "sale" : "split", plan.Occasion is not null ? "festive" : "pattern"];
        else if (image is not null)
            layouts = [plan.Badge is not null ? "sale" : "split", plan.Occasion is not null ? "festive" : "split", "minimal"];
        else
            layouts = ["pattern", "center", plan.Occasion is not null ? "festive" : "minimal"];
        if (plan.Occasion is not null && layouts[0] != "festive" && !layouts.Contains("festive"))
            layouts[2] = "festive";

        var headlines = new[] { plan.Headline, plan.AltHeadlines.ElementAtOrDefault(0) ?? plan.Headline, plan.AltHeadlines.ElementAtOrDefault(1) ?? plan.Headline };
        var reason = string.Join(" ", plan.Warnings);
        var ideas = new List<DesignIdea>();
        for (var i = 0; i < 3; i++)
        {
            var layout = layouts[i];
            var palette = layout == "minimal" ? "clean" : palettes[Math.Min(i, palettes.Count - 1)];
            // نسخهٔ سادهٔ روشن فقط «minimal» است؛ بقیه با پالت رنگی.
            if (palette == "clean" && layout != "minimal")
                palette = palettes.First(p => p != "clean");
            // دو نسخهٔ هم‌چیدمان (مثلاً split دوبار) ← دومی پالت دیگری می‌گیرد تا واقعاً متفاوت باشد.
            if (i > 0 && layouts[i] == layouts[i - 1] && ideas[i - 1].Spec.Palette == palette)
                palette = palettes[Math.Min(i + 1, palettes.Count - 1)];
            var withImage = layout switch
            {
                "photo" => photo,
                "pattern" or "center" => null,
                _ => image ?? pictures.FirstOrDefault(),
            };
            var spec = new DesignSpec(layout, palette, headlines[i], plan.Subline, plan.Badge, string.IsNullOrWhiteSpace(plan.ButtonText) ? null : plan.ButtonText,
                withImage, layout == "collage" ? null : price, layout == "collage" ? null : oldPrice, Kicker(plan), layout == "collage" ? pictures : null);
            ideas.Add(new DesignIdea($"brief:{productKey}:{hash}:{i + 1}", $"{plan.Title} – نسخهٔ {DesignTemplates.FaDigits((i + 1).ToString(CultureInfo.InvariantCulture))}",
                string.IsNullOrWhiteSpace(reason) ? "ساخته‌شده از ایدهٔ شما با دادهٔ واقعی فروشگاه." : reason,
                spec, plan.LinkUrl, plan.ExpiresAt, plan.Hero?.Id));
        }
        return ideas;
    }

    /// <summary>دور ۲۶: برچسب کوچک بالای تیتر (کپسول رنگی). مثال: مناسبت یلدا ← «شب یلدا»؛ حراج ← «حراج»؛ دسته ← «دسته‌بندی ویژه».</summary>
    public static string? Kicker(BriefPlan plan) => plan.Kind switch
    {
        BriefKind.Occasion when plan.Occasion is not null => Occasions.First(o => o.Key == plan.Occasion).Name,
        BriefKind.Deal => "حراج",
        BriefKind.Campaign => "جشنواره",
        BriefKind.Club => "باشگاه مشتریان",
        BriefKind.GiftCard => "هدیه",
        BriefKind.Blog => "راهنمای خرید",
        BriefKind.Product => "پیشنهاد ویژه",
        BriefKind.Category => "منتخب",
        _ => null,
    };

    /// <summary>گروه A/B نسخه‌های یک ایده (از کلید ایده). مثال: «brief:-:a1b2c3d4:2» ← «brief-a1b2c3d4»؛ کلید دیگر ← null.</summary>
    public static string? VariantGroup(string? ideaKey)
    {
        if (string.IsNullOrWhiteSpace(ideaKey) || !ideaKey.StartsWith("brief:", StringComparison.Ordinal))
            return null;
        var parts = ideaKey.Split(':');
        return parts.Length >= 3 && parts[2].Length > 0 ? "brief-" + parts[2] : null;
    }

    private static IdeaProduct? MatchProduct(string normalized, IReadOnlyList<IdeaProduct> products)
    {
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2 && !StopWords.Contains(w)).ToHashSet(StringComparer.Ordinal);
        if (words.Count < 2)
            return null;
        return products
            .Select(p => (Product: p, Hits: Normalize(p.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Count(words.Contains)))
            .Where(x => x.Hits >= 2 && x.Hits * 2 >= words.Count)
            .OrderByDescending(x => x.Hits).ThenByDescending(x => x.Product.Sales)
            .Select(x => x.Product)
            .FirstOrDefault();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "برای", "با", "و", "در", "به", "از", "تخفیف", "حراج", "بنر", "ویژه", "جدید", "یلدا", "نوروز", "عید", "هدیه", "روز", "خرید", "درصد",
    };

    private static bool ContainsAny(string text, params string[] words) => words.Any(w => text.Contains(w, StringComparison.Ordinal));

    /// <summary>وجود عبارت به‌صورت کلمهٔ کامل (نه بخشی از کلمهٔ دیگر). مثال: «مهر» در «مهربان» ← false.</summary>
    private static bool ContainsWord(string text, string phrase)
    {
        if (phrase.Length == 0)
            return false;
        var index = 0;
        while ((index = text.IndexOf(phrase, index, StringComparison.Ordinal)) >= 0)
        {
            var beforeOk = index == 0 || text[index - 1] == ' ';
            var end = index + phrase.Length;
            var afterOk = end == text.Length || text[end] == ' ' || text.AsSpan(end).StartsWith(" ها") || text.AsSpan(end).StartsWith("ها");
            if (beforeOk && afterOk)
                return true;
            index = end;
        }
        return false;
    }

    private static string Plural(string name) => name + " ها";

    private static string Hash(string text)
    {
        // FNV-1a ۳۲ بیتی – پایدار بین اجراها (string.GetHashCode تصادفی است).
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
