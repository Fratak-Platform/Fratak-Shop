using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Smart;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۳ – درخت دسته‌ها کاملاً دست سیستم
//  ۱) «خانواده» = دستهٔ اصلی (سطح بالا) هر نوع کالا. مثال: پاوربانک ← «لوازم جانبی موبایل».
//  ۲) آیکون خودکار هر دسته (نام آیکون از مجموعهٔ آیکون‌های سایت).
//  ۳) ممیز درخت (هر ساعت): یکی کردن دسته‌های تکراری با ریدایرکت ۳۰۱، جابه‌جایی دستهٔ جای‌غلط (حتی ساختهٔ مدیر)،
//     شکستن دستهٔ شلوغ به زیردسته، پنهان کردن دستهٔ خودکارِ خالی.
//  آدرس دسته (/category/{slug}) به والد بستگی ندارد؛ پس جابه‌جایی در درخت هیچ آدرسی را خراب نمی‌کند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک «خانواده» (دستهٔ اصلی). <paramref name="Aliases"/>: نام‌های هم‌معنی که اگر دستهٔ سطح‌بالایی دقیقاً با یکی از آن‌ها
/// در فروشگاه باشد، همان استفاده می‌شود (مقایسهٔ کامل، نه «شامل بودن»؛ تا «لوازم جانبی خودرو» با «لوازم جانبی» یکی گرفته نشود).
/// </summary>
public sealed record CategoryFamily(string Key, string Name, string Icon, string[] Aliases);

/// <summary>
/// فهرست خانواده‌ها و نگاشت «نوع کالا ← خانواده». استفاده: ساخت دستهٔ تازه (<see cref="SmartCategoryService"/>) و ممیز درخت.
/// </summary>
public static class CategoryFamilies
{
    public static readonly IReadOnlyList<CategoryFamily> All =
    [
        new("digital", "کالای دیجیتال", "Devices", ["کالای دیجیتال", "دیجیتال", "کالاهای دیجیتال"]),
        new("mobile-tablet", "موبایل و تبلت", "DeviceMobile", ["موبایل و تبلت", "موبایل", "گوشی و تبلت", "گوشی موبایل و تبلت"]),
        new("mobile-accessories", "لوازم جانبی موبایل", "Plug", ["لوازم جانبی موبایل", "لوازم جانبی گوشی", "لوازم جانبی", "جانبی موبایل"]),
        new("computer", "کامپیوتر و لپ‌تاپ", "Desktop", ["کامپیوتر و لپ تاپ", "کامپیوتر", "رایانه", "لپ تاپ و کامپیوتر", "کامپیوتر و تجهیزات جانبی"]),
        new("audio-video", "صوتی و تصویری", "SpeakerHigh", ["صوتی و تصویری", "صوتی", "صوتی تصویری", "لوازم صوتی"]),
        new("home-appliances", "لوازم خانگی", "Home", ["لوازم خانگی", "خانه و آشپزخانه", "لوازم خانگی برقی", "خانه و آشپزخانه"]),
        new("personal-care", "آرایشی و بهداشتی", "Sparkle", ["آرایشی و بهداشتی", "لوازم شخصی", "آرایشی بهداشتی", "زیبایی و سلامت"]),
        new("personal-electric", "لوازم شخصی برقی", "Heart", ["لوازم شخصی برقی", "سلامت", "لوازم برقی شخصی"]),
        new("electrical", "لوازم برقی و روشنایی", "Lightning", ["لوازم برقی و روشنایی", "لوازم برقی", "برق و روشنایی", "ابزار و برق"]),
        new("fashion", "مد و پوشاک", "ShoppingBag", ["مد و پوشاک", "پوشاک", "مد و لباس"]),
        new("toys", "اسباب‌بازی و کودک", "Gift", ["اسباب بازی و کودک", "اسباب بازی", "کودک و نوزاد"]),
    ];

    // نوع کالا ← خانواده (بر اساس اولین «راهنمای والد» نوع؛ برای نوع‌های دور ۲۲ صریح).
    private static readonly Dictionary<string, string> ByFirstHint = new(StringComparer.Ordinal)
    {
        ["کالای دیجیتال"] = "digital",
        ["موبایل"] = "mobile-tablet",
        ["لوازم جانبی"] = "mobile-accessories",
        ["کامپیوتر"] = "computer",
        ["صوتی"] = "audio-video",
        ["لوازم خانگی"] = "home-appliances",
        ["لوازم شخصی"] = "personal-care",
        ["مد و پوشاک"] = "fashion",
        ["لوازم شخصی برقی"] = "personal-electric",
        ["لوازم برقی"] = "electrical",
        ["اسباب بازی"] = "toys",
    };

    /// <summary>خانوادهٔ یک نوع کالا. مثال: پاوربانک ← «لوازم جانبی موبایل»؛ ماساژور ← «لوازم شخصی برقی»؛ تلویزیون ← «لوازم خانگی».</summary>
    public static CategoryFamily? For(ProductType? type)
    {
        if (type is null || type.ParentHints.Length == 0)
            return null;
        return ByFirstHint.TryGetValue(type.ParentHints[0], out var key) ? Find(key) : null;
    }

    /// <summary>خانواده با شناسه. مثال: «toys» ← «اسباب‌بازی و کودک».</summary>
    public static CategoryFamily? Find(string? key) => All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// این نام (یا نامک) یک خانواده است؟ مقایسهٔ کامل بعد از یکسان‌سازی فارسی. مثال: «لوازم جانبی گوشی» ← لوازم جانبی موبایل؛
    /// «لوازم جانبی خودرو» ← null.
    /// </summary>
    public static CategoryFamily? Match(string? name, string? slug = null)
    {
        if (!string.IsNullOrWhiteSpace(slug) && Find(slug.ToLowerInvariant()) is { } bySlug)
            return bySlug;
        var normal = PersianText.Normalize(name);
        if (normal.Length == 0)
            return null;
        return All.FirstOrDefault(f => PersianText.Normalize(f.Name) == normal || f.Aliases.Any(a => PersianText.Normalize(a) == normal));
    }
}

/// <summary>
/// تشخیص «دقیق» نوع یک دسته (سخت‌گیرتر از <see cref="ProductTypes.NameMatches"/>): نامک برابر شناسهٔ نوع، یا نام دقیقاً برابر نام
/// نوع یا یکی از کلیدواژه‌هایش. مثال: «هدفون» ← هدفون؛ «پاوربانک‌ها» ← پاوربانک؛ «موبایل و تبلت» ← null (خانواده است، نه نوع).
/// </summary>
public static class CategoryTypes
{
    public static ProductType? Strict(string? name, string? slug = null)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var bare = System.Text.RegularExpressions.Regex.Replace(slug.ToLowerInvariant(), @"-\d+$", "");
            if (ProductTypes.Find(bare) is { } bySlug)
                return bySlug;
        }
        var normal = PersianText.Normalize(name);
        if (normal.Length == 0)
            return null;
        var stemmed = string.Join(' ', PersianText.Tokenize(name).Select(PersianText.Stem));
        foreach (var type in ProductTypes.All)
        {
            if (PersianText.Normalize(type.Name) == normal)
                return type;
            foreach (var keyword in type.Keywords)
            {
                var k = PersianText.Normalize(keyword);
                if (k == normal || k == stemmed)
                    return type;
            }
        }
        return null;
    }
}

/// <summary>
/// آیکون خودکار دسته (نام آیکون مجموعهٔ سایت؛ <c>Icons.Svg</c>). مثال: «گوشی موبایل» ← DeviceMobile؛ «پاوربانک» ← Lightning؛
/// «لوازم خانگی» ← Home؛ نام ناشناخته ← Tag.
/// </summary>
public static class CategoryIcons
{
    public const string Default = "Tag";

    private static readonly Dictionary<string, string> ByType = new(StringComparer.Ordinal)
    {
        ["mobile-phone"] = "DeviceMobile", ["tablet"] = "DeviceMobile", ["laptop"] = "Desktop", ["smartwatch"] = "Clock", ["smart-band"] = "Pulse",
        ["wireless-earbuds"] = "Headset", ["headphone"] = "Headset", ["handsfree"] = "Headset", ["power-bank"] = "Lightning", ["charger"] = "Plug",
        ["cable"] = "Plug", ["phone-case"] = "DeviceMobile", ["screen-protector"] = "Shield", ["speaker"] = "SpeakerHigh", ["monitor"] = "Monitor",
        ["television"] = "Monitor", ["game-console"] = "Cube", ["gamepad"] = "Cube", ["mouse"] = "Cpu", ["keyboard"] = "Keyboard",
        ["flash-drive"] = "HardDrives", ["memory-card"] = "HardDrives", ["external-storage"] = "HardDrives", ["router"] = "Globe", ["camera"] = "VideoCamera",
        ["printer"] = "Printer", ["hair-dryer"] = "Sparkle", ["shaver"] = "Sparkle", ["air-fryer"] = "Home", ["vacuum-cleaner"] = "Home",
        ["refrigerator"] = "Home", ["washing-machine"] = "Home", ["tea-maker"] = "Home", ["coffee-maker"] = "Home", ["blender"] = "Home", ["iron"] = "Home",
        ["perfume"] = "Sparkle", ["wristwatch"] = "Clock", ["bag"] = "ShoppingBag", ["shoes"] = "ShoppingBag", ["massager"] = "Heart",
        ["power-strip"] = "Plug", ["hair-straightener"] = "Sparkle", ["flashlight"] = "Lightbulb", ["toy"] = "Gift",
        ["kitchenware"] = "Home",
    };

    /// <summary>نام آیکون برای نام/نامک دسته.</summary>
    public static string For(string? name, string? slug = null)
    {
        if (CategoryFamilies.Match(name, slug) is { } family)
            return family.Icon;
        var type = CategoryTypes.Strict(name, slug) ?? ProductTypes.DetectHead(name);
        if (type is not null && ByType.TryGetValue(type.Key, out var icon))
            return icon;
        var normal = PersianText.Normalize(name);
        if (normal.Contains("خانه", StringComparison.Ordinal) || normal.Contains("آشپزخانه", StringComparison.Ordinal))
            return "Home";
        if (normal.Contains("هدیه", StringComparison.Ordinal))
            return "Gift";
        if (normal.Contains("ورزش", StringComparison.Ordinal) || normal.Contains("سلامت", StringComparison.Ordinal))
            return "Heart";
        if (normal.Contains("کتاب", StringComparison.Ordinal) || normal.Contains("لوازم تحریر", StringComparison.Ordinal))
            return "BookOpen";
        if (normal.Contains("ابزار", StringComparison.Ordinal))
            return "Wrench";
        return Default;
    }
}

/// <summary>نتیجهٔ یک دور ممیزی درخت. <see cref="Summary"/> برای اعلان پنل.</summary>
public sealed record TreeAuditReport(int Merged, int Reparented, int Hidden, int Split, int Iconed, IReadOnlyList<string> Notes)
{
    public static readonly TreeAuditReport Empty = new(0, 0, 0, 0, 0, []);

    /// <summary>کاری انجام شد؟ (آیکون تنها اعلان نمی‌خواهد.)</summary>
    public bool Any => Merged + Reparented + Hidden + Split > 0;

    public string Summary => !Any
        ? "درخت دسته‌ها مرتب است."
        : string.Join("؛ ", new[]
        {
            Merged > 0 ? $"{PersianText.Digits(Merged)} دستهٔ تکراری یکی شد (با ریدایرکت ۳۰۱)" : null,
            Reparented > 0 ? $"{PersianText.Digits(Reparented)} دسته به جای درست درخت رفت" : null,
            Split > 0 ? $"{PersianText.Digits(Split)} کالا از دستهٔ شلوغ به زیردستهٔ دقیق رفت" : null,
            Hidden > 0 ? $"{PersianText.Digits(Hidden)} دستهٔ خالی پنهان شد" : null,
        }.Where(x => x is not null)) + ".";
}

/// <summary>ممیز درخت دسته‌ها (هر ساعت از کارگر هوشمند).</summary>
public interface ICategoryTreeService
{
    /// <summary>یک دور کامل ممیزی. <paramref name="now"/> برای «دستهٔ خالی قدیمی‌تر از ۳ روز».</summary>
    Task<TreeAuditReport> AuditAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>
/// ممیز درخت دسته‌ها. همه‌چیز محافظه‌کارانه: هیچ دسته‌ای پاک فیزیکی نمی‌شود (فقط حذف نرم پس از یکی شدن، با ریدایرکت ۳۰۱)،
/// چرخه در درخت ساخته نمی‌شود و کالای دستیِ مدیر فقط به «زیردستهٔ همان دسته» (دقیق‌تر، نه جای دیگر) می‌رود.
/// </summary>
public sealed class CategoryTreeService(FratakShopDbContext context) : ICategoryTreeService
{
    /// <summary>دستهٔ دارای این تعداد کالا یا بیشتر «شلوغ» است و بررسی می‌شود که زیردستهٔ دقیق لازم دارد یا نه.</summary>
    public const int CrowdedSize = 24;

    /// <summary>دستهٔ خودکارِ خالی بعد از این مدت پنهان می‌شود (فرصت برای کالای تازه).</summary>
    public static readonly TimeSpan EmptyGrace = TimeSpan.FromDays(3);

    public async Task<TreeAuditReport> AuditAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var categories = await context.Categories.AsTracking().ToListAsync(cancellationToken);
        if (categories.Count == 0)
            return TreeAuditReport.Empty;
        var counts = await context.Products.GroupBy(p => p.CategoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var notes = new List<string>();

        var iconed = 0;
        foreach (var category in categories.Where(c => string.IsNullOrWhiteSpace(c.IconKey) && !c.IsSystem))
        {
            category.IconKey = CategoryIcons.For(category.Name, category.Slug);
            iconed++;
        }

        var merged = await MergeDuplicatesAsync(categories, counts, notes, cancellationToken);
        var reparented = Reparent(categories, notes);
        var split = await SplitCrowdedAsync(categories, counts, notes, cancellationToken);
        var hidden = HideEmpty(categories, counts, now);

        if (context.ChangeTracker.HasChanges())
            await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        if (merged + reparented + split + hidden > 0)
            DataChangeNotifier.Publish(typeof(Category), typeof(Product));
        return new TreeAuditReport(merged, reparented, hidden, split, iconed, notes);
    }

    // ── ۱) یکی کردن تکراری‌ها ─────────────────────────────────────────

    /// <summary>
    /// کلید یکسانی دو دسته: نوع دقیق یکسان («هدفون» و «هدفون و هدست») یا همان واژه‌ها («ماساژورها» و «ماساژور»).
    /// مثال: Key(«ماساژورها») == Key(«ماساژور»)؛ Key(«لوازم جانبی») != Key(«لوازم جانبی موبایل»).
    /// </summary>
    public static string DuplicateKey(string name, string? slug)
    {
        if (CategoryTypes.Strict(name, slug) is { } type)
            return "t:" + type.Key;
        var terms = CategoryClassifier.Terms(name).OrderBy(t => t, StringComparer.Ordinal);
        return "n:" + string.Join(' ', terms);
    }

    /// <summary>کلید گروه یکی‌شدن: نوع دقیق ← سراسری؛ نام عمومی ← همراه شناسهٔ والد.</summary>
    private static string GroupKey(Category category)
    {
        var key = DuplicateKey(category.Name, category.Slug);
        return key.StartsWith("t:", StringComparison.Ordinal) ? key : key + "|" + category.ParentId;
    }

    private async Task<int> MergeDuplicatesAsync(List<Category> categories, Dictionary<Guid, int> counts, List<string> notes, CancellationToken cancellationToken)
    {
        var merged = 0;
        var live = categories.Where(c => !c.IsSystem && !c.IsDeleted && !CategoryClassifier.IsCatchAll(c.Name, c.Slug)).ToList();
        // «نوع» دقیق در کل فروشگاه یکی است؛ نام‌های عمومی فقط زیر یک والد مشترک یکی می‌شوند («لوازم جانبی» زیر «خودرو» و زیر «موبایل» دو چیزند).
        foreach (var group in live.GroupBy(GroupKey)
                     .Where(g => !g.Key.StartsWith("n:|", StringComparison.Ordinal) && g.Count() > 1))
        {
            var members = group.ToList();
            // دسته و زیردستهٔ خودش هرگز یکی نمی‌شوند (مثلاً «ماساژور» زیر «ماساژور» – بهتر است مدیر ببیند).
            if (members.Any(a => members.Any(b => a != b && IsAncestor(categories, a.Id, b.Id))))
                continue;
            var keep = members.OrderByDescending(c => counts.GetValueOrDefault(c.Id)).ThenBy(c => c.IsAutoCreated).ThenBy(c => c.CreatedAt).First();
            foreach (var other in members.Where(c => c != keep))
            {
                await context.Products.Where(p => p.CategoryId == other.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, keep.Id).SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
                counts[keep.Id] = counts.GetValueOrDefault(keep.Id) + counts.GetValueOrDefault(other.Id);
                counts[other.Id] = 0;
                foreach (var child in categories.Where(c => c.ParentId == other.Id && c != keep))
                    child.ParentId = keep.Id;
                if (string.IsNullOrWhiteSpace(keep.Description) && !string.IsNullOrWhiteSpace(other.Description))
                    keep.Description = other.Description;
                if (string.IsNullOrWhiteSpace(keep.ImageName) && !string.IsNullOrWhiteSpace(other.ImageName))
                    keep.ImageName = other.ImageName;
                if (other.IsActive && !keep.IsActive)
                    keep.IsActive = true;
                await RedirectAsync(other.Slug, keep.Slug, cancellationToken);
                context.Categories.Remove(other);
                other.IsDeleted = true; // برای بقیهٔ همین دور (در فهرست حافظه) دیگر حساب نشود
                notes.Add($"«{other.Name}» با «{keep.Name}» یکی شد.");
                merged++;
            }
        }
        categories.RemoveAll(c => c.IsDeleted);
        return merged;
    }

    /// <summary>ریدایرکت ۳۰۱ آدرس دستهٔ یکی‌شده به دستهٔ ماندگار (ریدایرکت قبلیِ همان آدرس به‌روز می‌شود).</summary>
    private async Task RedirectAsync(string fromSlug, string toSlug, CancellationToken cancellationToken)
    {
        var from = ("/category/" + fromSlug).TrimEnd('/').ToLowerInvariant();
        var to = "/category/" + Uri.EscapeDataString(toSlug);
        var existing = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(r => r.FromPath == from, cancellationToken);
        if (existing is not null)
        {
            existing.ToUrl = to;
            existing.IsActive = true;
            return;
        }
        context.SeoRedirects.Add(new SeoRedirect { FromPath = from, ToUrl = to, Type = RedirectType.MovedPermanently, Notes = "خودکار: یکی شدن دسته‌های تکراری", IsActive = true });
    }

    // ── ۲) جای درست در درخت ──────────────────────────────────────────

    /// <summary>
    /// دستهٔ «نوع» (پاوربانک، ماساژور…) باید زیر خانوادهٔ خودش باشد. جای غلط یعنی: سطح بالا، زیر دستهٔ سیستمی/سایر، یا زیر دستهٔ
    /// نوعِ دیگری (مثلاً «پاوربانک» زیر «هدفون»). خانواده اگر نبود ساخته می‌شود. خانواده‌ای که زیر یک دستهٔ «نوع» افتاده ← سطح بالا.
    /// مثال: «ماساژور» در سطح بالا ← زیر «لوازم شخصی برقی».
    /// </summary>
    private int Reparent(List<Category> categories, List<string> notes)
    {
        var moved = 0;
        foreach (var category in categories.Where(c => !c.IsSystem).ToList())
        {
            var parent = category.ParentId is { } pid ? categories.FirstOrDefault(c => c.Id == pid) : null;
            if (CategoryTypes.Strict(category.Name, category.Slug) is { } type && CategoryFamilies.For(type) is { } family)
            {
                var parentIsFamily = parent is not null && CategoryFamilies.Match(parent.Name, parent.Slug) is not null;
                var parentOtherType = parent is not null && CategoryTypes.Strict(parent.Name, parent.Slug) is { } pt && pt.Key != type.Key;
                var wrong = parent is null || parent.IsSystem || CategoryClassifier.IsCatchAll(parent.Name, parent.Slug) || (parentOtherType && !parentIsFamily);
                if (!wrong)
                    continue;
                var home = FamilyCategory(categories, family, category.IsActive);
                if (home.Id == category.Id || IsAncestor(categories, category.Id, home.Id))
                    continue;
                category.ParentId = home.Id;
                notes.Add($"«{category.Name}» زیر «{home.Name}» رفت.");
                moved++;
            }
            else if (parent is not null && CategoryFamilies.Match(category.Name, category.Slug) is not null
                     && CategoryTypes.Strict(parent.Name, parent.Slug) is not null)
            {
                category.ParentId = null;
                notes.Add($"«{category.Name}» دستهٔ اصلی شد.");
                moved++;
            }
        }
        return moved;
    }

    /// <summary>دستهٔ موجود خانواده (سطح بالا) یا ساخت آن. مثال: خانوادهٔ «لوازم جانبی موبایل» نیست ← ساخته می‌شود (نامک mobile-accessories).</summary>
    private Category FamilyCategory(List<Category> categories, CategoryFamily family, bool active)
    {
        var existing = categories.FirstOrDefault(c => !c.IsSystem && c.ParentId == null && CategoryFamilies.Match(c.Name, c.Slug) == family)
                       ?? categories.FirstOrDefault(c => !c.IsSystem && CategoryFamilies.Match(c.Name, c.Slug) == family);
        if (existing is not null)
        {
            if (active && !existing.IsActive)
                existing.IsActive = true;
            return existing;
        }
        var taken = categories.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entity = new Category
        {
            Name = family.Name,
            Slug = EnglishSlug.Unique(family.Key, taken.Contains, 200),
            IsActive = active,
            IsAutoCreated = true,
            DisplayOrder = 100,
            IconKey = family.Icon,
        };
        context.Categories.Add(entity);
        categories.Add(entity);
        return entity;
    }

    // ── ۳) دستهٔ شلوغ ← زیردسته ──────────────────────────────────────

    /// <summary>
    /// در دستهٔ شلوغ (≥ <see cref="CrowdedSize"/> کالا) کالاهایی که نوعشان با خود دسته فرق دارد به دستهٔ دقیق نوعشان می‌روند
    /// (اگر نبود، زیر همین دسته ساخته می‌شود؛ همان «جزئی‌تر شدن»). کالای دستیِ مدیر فقط وقتی جابه‌جا می‌شود که مقصد زیردستهٔ
    /// همین دسته باشد. مثال: ۳۰ کالا در «لوازم جانبی» که ۸ تا پاوربانک‌اند ← «پاوربانک» زیر «لوازم جانبی».
    /// </summary>
    private async Task<int> SplitCrowdedAsync(List<Category> categories, Dictionary<Guid, int> counts, List<string> notes, CancellationToken cancellationToken)
    {
        var moved = 0;
        var crowded = categories.Where(c => !c.IsSystem && counts.GetValueOrDefault(c.Id) >= CrowdedSize).Take(20).ToList();
        foreach (var category in crowded)
        {
            var own = CategoryTypes.Strict(category.Name, category.Slug);
            var products = await context.Products.Where(p => p.CategoryId == category.Id)
                .Select(p => new { p.Id, p.Name, p.CategorySource, p.IsActive }).Take(2000).ToListAsync(cancellationToken);
            foreach (var group in products.Select(p => (Product: p, Type: ProductTypes.Detect(p.Name))).Where(x => x.Type is not null && x.Type.Key != own?.Key)
                         .GroupBy(x => x.Type!.Key))
            {
                var type = group.First().Type!;
                var target = categories.FirstOrDefault(c => c.ParentId == category.Id && CategoryTypes.Strict(c.Name, c.Slug)?.Key == type.Key)
                             ?? categories.FirstOrDefault(c => !c.IsSystem && c.Id != category.Id && CategoryTypes.Strict(c.Name, c.Slug)?.Key == type.Key);
                if (target is null)
                {
                    var taken = categories.Select(c => c.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    target = new Category
                    {
                        Name = SmartCategoryService.Display(type.Name),
                        Slug = EnglishSlug.Unique(type.Key, taken.Contains, 200),
                        ParentId = category.Id,
                        IsActive = group.Any(x => x.Product.IsActive),
                        IsAutoCreated = true,
                        DisplayOrder = 500,
                        IconKey = CategoryIcons.For(type.Name, type.Key),
                    };
                    context.Categories.Add(target);
                    categories.Add(target);
                    await context.SaveChangesAsync(cancellationToken);
                }
                var isChild = target.ParentId == category.Id;
                var ids = group.Where(x => isChild || x.Product.CategorySource != ProductCategorySource.Manual).Select(x => x.Product.Id).ToList();
                if (ids.Count == 0)
                    continue;
                var targetId = target.Id;
                moved += await context.Products.Where(p => ids.Contains(p.Id) && p.CategoryId == category.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, targetId).SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
                counts[category.Id] = counts.GetValueOrDefault(category.Id) - ids.Count;
                counts[targetId] = counts.GetValueOrDefault(targetId) + ids.Count;
                notes.Add($"{PersianText.Digits(ids.Count)} کالا از «{category.Name}» به «{target.Name}» رفت.");
            }
        }
        return moved;
    }

    // ── ۴) دستهٔ خالی ────────────────────────────────────────────────

    /// <summary>دستهٔ «خودکار»، فعال، بدون کالا و بدون زیردسته، قدیمی‌تر از ۳ روز ← پنهان (حذف نمی‌شود؛ با کالای تازه دوباره فعال می‌شود).</summary>
    private static int HideEmpty(List<Category> categories, Dictionary<Guid, int> counts, DateTimeOffset now)
    {
        var hidden = 0;
        foreach (var category in categories.Where(c => c.IsAutoCreated && !c.IsSystem && c.IsActive && c.CreatedAt != default && c.CreatedAt < now - EmptyGrace))
        {
            if (counts.GetValueOrDefault(category.Id) > 0 || categories.Any(c => c.ParentId == category.Id && c.IsActive))
                continue;
            category.IsActive = false;
            hidden++;
        }
        return hidden;
    }

    /// <summary>آیا <paramref name="ancestorId"/> جد (والد، والدِ والد …) <paramref name="id"/> است؟ (با سقف عمق برای جلوگیری از حلقهٔ داده‌های خراب.)</summary>
    public static bool IsAncestor(IReadOnlyList<Category> categories, Guid ancestorId, Guid id)
    {
        var current = categories.FirstOrDefault(c => c.Id == id)?.ParentId;
        for (var depth = 0; current is { } parent && depth < 32; depth++)
        {
            if (parent == ancestorId)
                return true;
            current = categories.FirstOrDefault(c => c.Id == parent)?.ParentId;
        }
        return false;
    }
}
