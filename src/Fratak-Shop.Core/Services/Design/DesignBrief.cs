using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
public sealed record BriefCategory(Guid Id, string Name, string Slug, int InStock);

/// <summary>برند برای تطبیق متن ایده.</summary>
public sealed record BriefBrand(Guid Id, string Name, string? EnglishName, string Slug, int InStock);

/// <summary>
/// خواستهٔ خوانده‌شده از متن کوتاه. مثال: «تخفیف هدفون یلدا ۲۰٪» ← Kind=Occasion، Occasion=yalda، Percent=20، Deal=true.
/// </summary>
public sealed record BriefIntent(BriefKind Kind, string? Occasion, int? Percent, bool Deal, string Normalized);

/// <summary>دادهٔ واقعی مرتبط با ایده (از پایگاه داده؛ DesignService.BriefDataAsync).</summary>
public sealed record BriefData(IReadOnlyList<IdeaProduct> Products, BriefCategory? Category, BriefBrand? Brand, string SiteName);

/// <summary>
/// برنامهٔ کامل بنر: تیتر، متن، نشان (فقط تخفیف واقعی)، دکمه، لینک، کالای اصلی و هشدارها (مثال: «۵۰٪ گفته بودید ولی بیشترین تخفیف واقعی ۲۰٪ است»).
/// </summary>
public sealed record BriefPlan(BriefKind Kind, string Title, string Headline, string? Subline, string? Badge, string ButtonText, string LinkUrl,
    IdeaProduct? Hero, IReadOnlyList<IdeaProduct> Products, string Palette, IReadOnlyList<string> Warnings, string? Occasion, DateTimeOffset? ExpiresAt);

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

        BriefKind kind;
        if (ContainsAny(normalized, "کارت هدیه", "گیفت کارت", "gift card", "giftcard"))
            kind = BriefKind.GiftCard;
        else if (ContainsAny(normalized, "باشگاه", "امتیاز", "club"))
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
        return new BriefIntent(kind, occasion, percent, deal, normalized);
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
    public static BriefPlan Plan(string text, BriefIntent intent, BriefData data, DateTimeOffset now)
    {
        var warnings = new List<string>();
        var products = data.Products.Where(p => p.Stock > 0 && p.FinalPrice > 0).ToList();
        var site = string.IsNullOrWhiteSpace(data.SiteName) ? "فروشگاه" : data.SiteName.Trim();
        var occasion = intent.Occasion is null ? default : Occasions.First(o => o.Key == intent.Occasion);
        var category = data.Category;
        var brand = data.Brand;

        // کالای مشخص: متن دست‌کم ۲ کلمهٔ معنادار از نام یک کالا را دارد.
        var product = intent.Kind is BriefKind.General or BriefKind.Deal or BriefKind.Occasion ? MatchProduct(intent.Normalized, products) : null;
        var kind = intent.Kind;
        if (kind == BriefKind.General)
            kind = product is not null ? BriefKind.Product : category is not null ? BriefKind.Category : brand is not null ? BriefKind.Brand : BriefKind.General;

        List<IdeaProduct> scope = product is not null && kind is BriefKind.Product or BriefKind.Deal ? new List<IdeaProduct> { product } : products;
        var maxDiscount = scope.Where(p => p.Stock >= 1).Select(p => p.DiscountPercent).DefaultIfEmpty(0).Max();
        string? badge = null;
        if (intent.Deal || kind == BriefKind.Deal)
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

        var shortTitle = DesignTemplates.Shorten(string.IsNullOrWhiteSpace(text) ? title : text.Trim(), 120);
        return new BriefPlan(kind, shortTitle, DesignTemplates.Shorten(headline, 60), subline is null ? null : DesignTemplates.Shorten(subline, 90), badge, button,
            DesignService.SafeLink(link) ?? "/products", hero, top, palette, warnings, occasion.Key, expires);
    }

    /// <summary>
    /// سه نسخهٔ متفاوت از یک برنامه (چیدمان و رنگ متفاوت). <paramref name="imageColor"/> = رنگ غالب عکس کالا (اختیاری)؛
    /// اگر بود، نسخهٔ اول با «رنگ عکس کالا» ساخته می‌شود (پالت خوانا با کنتراست ≥ ۴٫۵).
    /// کلید هر نسخه «brief:{کالا یا -}:{هش}:{۱..۳}» است تا نسخه‌های منتشرشده خودکار گروه A/B شوند.
    /// </summary>
    public static List<DesignIdea> Variants(BriefPlan plan, string? imageColor, DesignImage? image, string? salt = null)
    {
        // salt (مثلاً زمان ساخت) ← ساخت دوبارهٔ همان ایده بعد از رد شدن، کلید تازه می‌گیرد.
        var hash = Hash(plan.Title + "|" + plan.Headline + "|" + plan.LinkUrl + "|" + salt);
        var productKey = plan.Hero?.Id.ToString("N") ?? "-";
        var price = plan.Hero is { } h && plan.Kind is BriefKind.Product or BriefKind.Deal ? DesignTemplates.Toman(h.FinalPrice) : null;
        var oldPrice = plan.Hero is { DiscountPercent: > 0 } d && price is not null ? DesignTemplates.Toman(d.Price) : null;

        var palettes = new List<string>();
        if (DesignColors.TryParse(imageColor, out _))
            palettes.Add(imageColor!.Trim().ToLowerInvariant());
        palettes.Add(plan.Palette);
        foreach (var fallback in new[] { "ocean", "night", "clean", "forest", "royal" })
        {
            if (!palettes.Contains(fallback))
                palettes.Add(fallback);
        }

        var first = plan.Occasion is not null ? "festive" : "split";
        var second = plan.Badge is not null ? "sale" : "center";
        var layouts = new[] { first, second, "minimal" };
        var reason = string.Join(" ", plan.Warnings);
        var ideas = new List<DesignIdea>();
        for (var i = 0; i < 3; i++)
        {
            var layout = layouts[i];
            var palette = layout == "minimal" ? "clean" : palettes[Math.Min(i, palettes.Count - 1)];
            // نسخهٔ سوم همیشه روشن و ساده؛ اگر پالت قبلی هم «clean» بود، پالت بعدی.
            if (i < 2 && palette == "clean" && layout != "minimal")
                palette = palettes.First(p => p != "clean");
            var spec = new DesignSpec(layout, palette, plan.Headline, plan.Subline, plan.Badge, plan.ButtonText,
                layout == "center" ? null : image, price, oldPrice);
            ideas.Add(new DesignIdea($"brief:{productKey}:{hash}:{i + 1}", $"{plan.Title} – نسخهٔ {DesignTemplates.FaDigits((i + 1).ToString(CultureInfo.InvariantCulture))}",
                string.IsNullOrWhiteSpace(reason) ? "ساخته‌شده از ایدهٔ شما با دادهٔ واقعی فروشگاه." : reason,
                spec, plan.LinkUrl, plan.ExpiresAt, plan.Hero?.Id));
        }
        return ideas;
    }

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
