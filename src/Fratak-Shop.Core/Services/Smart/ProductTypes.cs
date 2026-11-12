using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک «نوع کالا» که از نام محصول شناخته می‌شود (دور ۲۱ – دسته‌بندی هوشمند).
/// </summary>
/// <param name="Key">شناسهٔ انگلیسی که نامک دستهٔ ساخته‌شده هم می‌شود. مثال: «mobile-phone».</param>
/// <param name="Name">نام فارسی دسته. مثال: «گوشی موبایل».</param>
/// <param name="Important">کالای مهم/پرجست‌وجو: با اولین محصول هم دستهٔ خودش را می‌گیرد (منتظر ۳ کالای مشابه نمی‌ماند).</param>
/// <param name="Keywords">عبارت‌هایی که اول نام محصول می‌آیند (نرمال‌شده). مثال: «گوشی»، «موبایل»، «iphone».</param>
/// <param name="ParentHints">اگر دسته‌ای با این واژه‌ها در فروشگاه هست، دستهٔ تازه زیرمجموعهٔ آن ساخته می‌شود. مثال: «موبایل»، «دیجیتال».</param>
public sealed record ProductType(string Key, string Name, bool Important, string[] Keywords, string[] ParentHints)
{
    /// <summary>
    /// دور ۲۲: نام انگلیسی نوع از روی شناسه (برای «نام انگلیسی» کالا). مثال: «power-bank» ← «Power Bank»، «mobile-phone» ← «Mobile Phone».
    /// </summary>
    public string English => string.Join(' ', Key.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>
    /// دور ۲۲: واژهٔ اصلی نام نوع (بخش پیش از « و »). مثال: «هدفون و هدست» ← «هدفون»؛ «پاوربانک» ← «پاوربانک».
    /// برای افزودن نوع کالا به نامی که فقط انگلیسی است («Redmi Buds 4» ← «هدفون Redmi Buds 4»).
    /// </summary>
    public string Head => Name.Split(" و ", 2, StringSplitOptions.TrimEntries)[0];
}

/// <summary>
/// فرهنگ «نوع کالا» برای دسته‌بندی هوشمند، کاملاً داخلی و بدون سرویس بیرونی.
/// <para>
/// قاعدهٔ اصلی: در نام‌های فارسی فروشگاهی «نوع کالا» تقریباً همیشه اول می‌آید («گوشی موبایل سامسونگ …»، «قاب گوشی سامسونگ …»).
/// پس فقط نزدیک‌ترین عبارت شناخته‌شده به ابتدای نام (حداکثر واژهٔ سوم) حساب می‌شود؛ به همین دلیل «قاب گوشی» قاب است نه گوشی.
/// </para>
/// <para>محل استفاده: <see cref="CategoryClassifier"/> و <see cref="SmartCategoryService"/>.</para>
/// </summary>
public static class ProductTypes
{
    private static readonly string[] Digital = ["کالای دیجیتال", "دیجیتال"];
    private static readonly string[] Mobile = ["موبایل", "کالای دیجیتال", "دیجیتال"];
    private static readonly string[] MobileAccessories = ["لوازم جانبی", "جانبی موبایل", "موبایل"];
    private static readonly string[] Computer = ["کامپیوتر", "رایانه", "کالای دیجیتال", "دیجیتال"];
    private static readonly string[] Audio = ["صوتی", "لوازم جانبی", "کالای دیجیتال", "دیجیتال"];
    private static readonly string[] Home = ["لوازم خانگی", "خانه و آشپزخانه", "آشپزخانه", "خانگی"];
    private static readonly string[] Personal = ["لوازم شخصی", "آرایشی", "بهداشتی", "زیبایی"];
    private static readonly string[] Fashion = ["مد و پوشاک", "پوشاک", "اکسسوری"];

    /// <summary>همهٔ نوع‌ها. ترتیب مهم نیست؛ طولانی‌ترین عبارت در همان جایگاه برنده است.</summary>
    public static readonly IReadOnlyList<ProductType> All =
    [
        new("mobile-phone", "گوشی موبایل", true, ["گوشی موبایل", "گوشی هوشمند", "گوشی", "موبایل", "smartphone", "iphone", "آیفون"], Mobile),
        new("tablet", "تبلت", true, ["تبلت", "ipad", "آیپد", "tablet"], Mobile),
        new("laptop", "لپ‌تاپ", true, ["لپ تاپ", "لپتاپ", "نوت بوک", "laptop", "notebook", "مک بوک", "macbook"], Computer),
        new("smartwatch", "ساعت هوشمند", true, ["ساعت هوشمند", "smartwatch", "smart watch", "اسمارت واچ"], Digital),
        new("smart-band", "مچ‌بند هوشمند", false, ["مچ بند هوشمند", "دستبند هوشمند", "smart band"], Digital),
        new("wireless-earbuds", "هندزفری بی‌سیم", true, ["هندزفری بی سیم", "هندزفری بلوتوثی", "هندزفری بلوتوث", "ایرپاد", "ایربادز", "airpods", "earbuds"], Audio),
        new("headphone", "هدفون و هدست", true, ["هدفون", "هدست", "headphone", "headset"], Audio),
        new("handsfree", "هندزفری", false, ["هندزفری", "ایرفون", "earphone"], Audio),
        new("power-bank", "پاوربانک", true, ["پاوربانک", "پاور بانک", "شارژر همراه", "power bank", "powerbank"], MobileAccessories),
        new("charger", "شارژر و آداپتور", false, ["شارژر دیواری", "شارژر فندکی", "شارژر", "آداپتور", "charger", "adapter", "کلگی"], MobileAccessories),
        new("cable", "کابل و تبدیل", false, ["کابل", "تبدیل", "cable"], MobileAccessories),
        new("phone-case", "قاب و کاور گوشی", false, ["قاب", "کاور", "گارد"], MobileAccessories),
        new("screen-protector", "محافظ صفحه‌نمایش", false, ["محافظ صفحه", "گلس", "glass"], MobileAccessories),
        new("speaker", "اسپیکر", true, ["اسپیکر", "speaker", "بلندگو"], Audio),
        new("monitor", "مانیتور", true, ["مانیتور", "monitor"], Computer),
        new("television", "تلویزیون", true, ["تلویزیون", "تلوزیون"], Home),
        new("game-console", "کنسول بازی", true, ["کنسول بازی", "کنسول", "پلی استیشن", "playstation", "ps5", "xbox", "ایکس باکس"], Digital),
        new("gamepad", "دسته بازی", false, ["دسته بازی", "گیم پد", "gamepad", "جوی استیک"], Digital),
        new("mouse", "ماوس", false, ["ماوس", "موس", "mouse"], Computer),
        new("keyboard", "کیبورد", false, ["کیبورد", "صفحه کلید", "keyboard"], Computer),
        new("flash-drive", "فلش مموری", false, ["فلش مموری", "فلش", "flash"], Computer),
        new("memory-card", "کارت حافظه", false, ["کارت حافظه", "مموری کارت", "microsd", "micro sd"], Computer),
        new("external-storage", "هارد و حافظه اکسترنال", false, ["هارد اکسترنال", "هارد", "اس اس دی", "ssd"], Computer),
        new("router", "مودم و روتر", false, ["مودم", "روتر", "router", "modem"], Computer),
        new("camera", "دوربین", true, ["دوربین", "camera"], Digital),
        new("printer", "پرینتر", false, ["پرینتر", "چاپگر", "printer"], Computer),
        new("hair-dryer", "سشوار", false, ["سشوار"], Personal),
        new("shaver", "ماشین اصلاح", false, ["ماشین اصلاح", "ریش تراش", "موزن", "شیور", "trimmer"], Personal),
        new("air-fryer", "سرخ‌کن", false, ["سرخ کن", "هواپز", "air fryer"], Home),
        new("vacuum-cleaner", "جاروبرقی", true, ["جاروبرقی", "جارو برقی", "جارو شارژی"], Home),
        new("refrigerator", "یخچال و فریزر", true, ["یخچال فریزر", "یخچال", "فریزر"], Home),
        new("washing-machine", "ماشین لباسشویی", true, ["ماشین لباسشویی", "لباسشویی"], Home),
        new("tea-maker", "چای‌ساز و کتری برقی", false, ["چای ساز", "کتری برقی", "کتری"], Home),
        new("coffee-maker", "قهوه‌ساز", false, ["قهوه ساز", "اسپرسو ساز"], Home),
        new("blender", "مخلوط‌کن و غذاساز", false, ["مخلوط کن", "آبمیوه گیری", "غذاساز"], Home),
        new("iron", "اتو", false, ["اتو بخار", "اتو"], Home),
        new("perfume", "عطر و ادکلن", false, ["عطر", "ادکلن", "ادو پرفیوم", "ادوپرفیوم", "ادو تویلت", "بادی اسپلش"], Personal),
        new("wristwatch", "ساعت مچی", false, ["ساعت مچی", "ساعت"], Fashion),
        new("bag", "کیف و کوله‌پشتی", false, ["کوله پشتی", "کوله", "کیف"], Fashion),
        new("shoes", "کفش", false, ["کفش", "کتانی", "صندل"], Fashion),
        // دور ۲۲: نوع‌های تازه (نمونهٔ پیام کاربر: ماساژور گردن، سه‌راهی برق). «ماساژ» تنها عمداً کلیدواژه نیست («روغن ماساژ» ماساژور نیست).
        new("massager", "ماساژور", false, ["ماساژور", "ماساژر", "ماساژ دهنده", "ماساژدهنده", "تفنگ ماساژ", "massager", "massage gun"], ["لوازم شخصی برقی", "سلامت", "لوازم شخصی", "زیبایی"]),
        new("power-strip", "سه‌راهی و چندراهی برق", false, ["سه راهی برق", "چند راهی برق", "چندراهی برق", "سه راهی", "چندراهی", "چند راهی", "محافظ برق", "power strip"], ["لوازم برقی", "برق", "کالای دیجیتال", "دیجیتال"]),
        new("hair-straightener", "اتو مو و حالت‌دهنده", false, ["اتو مو", "اتوی مو", "اتو موی", "بابلیس", "حالت دهنده مو", "فر کننده مو"], Personal),
        new("flashlight", "چراغ قوه", false, ["چراغ قوه", "چراغ پیشانی", "flashlight"], ["لوازم برقی", "ابزار"]),
        new("toy", "اسباب‌بازی", false, ["اسباب بازی", "عروسک", "لگو"], ["اسباب بازی", "کودک"]),
        // دور ۲۹ (#۱۰): ظرف و سرویس آشپزخانه (نمونهٔ پیام کاربر: «ظرف شیش‌پارچه» که در «چهارراهی برق» افتاده بود).
        new("kitchenware", "ظروف آشپزخانه", false, ["ظرف", "ظروف", "سرویس ظروف", "سرویس پذیرایی", "قابلمه", "ماهیتابه", "ماهی تابه", "تابه",
            "پیرکس", "شیش پارچه", "شش پارچه", "بشقاب", "کاسه", "قندان", "ظرف نگهدارنده"], Home),
    ];

    /// <summary>واژه‌های بی‌معنی که گاهی پیش از نوع کالا می‌آیند و نباید «نوع» حساب شوند.</summary>
    public static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "خرید", "پک", "پکیج", "ست", "مجموعه", "جدید", "اورجینال", "اصل", "اصلی", "نو", "فروش", "ویژه", "قیمت", "کالای", "محصول",
        "عدد", "عددی", "تایی", "تکه",
    };

    // ایندکس واژهٔ اول هر عبارت ← (عبارت به شکل واژه‌ها، نوع) برای جست‌وجوی سریع.
    private static readonly Dictionary<string, List<(string[] Words, ProductType Type)>> Index = BuildIndex();

    private static Dictionary<string, List<(string[] Words, ProductType Type)>> BuildIndex()
    {
        var index = new Dictionary<string, List<(string[], ProductType)>>(StringComparer.Ordinal);
        foreach (var type in All)
        {
            foreach (var keyword in type.Keywords)
            {
                var words = PersianText.Tokenize(keyword).ToArray();
                if (words.Length == 0)
                    continue;
                if (!index.TryGetValue(words[0], out var list))
                    index[words[0]] = list = [];
                list.Add((words, type));
            }
        }
        foreach (var list in index.Values)
            list.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
        return index;
    }

    /// <summary>نوع با شناسهٔ انگلیسی. مثال: Find("tablet") ← تبلت.</summary>
    public static ProductType? Find(string? key) => All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// تشخیص نوع کالا از نام. فقط عبارتی که از یکی از سه واژهٔ معنادار اول شروع شود حساب می‌شود؛ واژه‌های پرکننده
    /// («خرید»، «پک»، «عدد») و عددها جزو این سه نیستند.
    /// <para>مثال‌ها: «گوشی موبایل سامسونگ Galaxy A55» ← گوشی موبایل؛ «قاب گوشی سامسونگ A55» ← قاب و کاور؛
    /// «خرید هدفون بی سیم شیائومی» ← هدفون؛ «پک ۳ عددی کابل شارژ» ← کابل؛ «لیوان سرامیکی» ← null (نوع شناخته‌شده نیست).</para>
    /// </summary>
    public static ProductType? Detect(string? title) => DetectWithin(title, 3);

    /// <summary>
    /// فقط «سرواژه» (اولین واژهٔ معنادار): برای نام دسته‌ها. مثال: «لوازم جانبی گوشی» ← null (سرواژه «لوازم» است)؛ «گوشی و تبلت» ← گوشی.
    /// </summary>
    public static ProductType? DetectHead(string? title) => DetectWithin(title, 1);

    private static ProductType? DetectWithin(string? title, int budget)
    {
        var words = PersianText.Tokenize(title);
        var meaningful = 0;
        for (var position = 0; position < words.Count && meaningful < budget; position++)
        {
            if (IsSkippable(words[position]))
                continue;
            var match = MatchAt(words, position);
            if (match is not null)
                return match;
            meaningful++;
        }
        return null;
    }

    /// <summary>پرکننده یا عدد («پک»، «۳»، «عددی»، «2x»)؛ در شمارش سه واژهٔ اول حساب نمی‌شود.</summary>
    private static bool IsSkippable(string word) =>
        Fillers.Contains(word) || word.All(char.IsDigit) || word.Length <= 3 && char.IsDigit(word[0]);

    /// <summary>آیا عبارت این نوع از جایگاه <paramref name="position"/> شروع می‌شود؟ (همچنین شکل جمع: «قاب‌های» ← «قاب»)</summary>
    private static ProductType? MatchAt(List<string> words, int position)
    {
        foreach (var candidate in new[] { words[position], PersianText.Stem(words[position]) }.Distinct())
        {
            if (!Index.TryGetValue(candidate, out var list))
                continue;
            foreach (var (phrase, type) in list)
            {
                if (position + phrase.Length > words.Count)
                    continue;
                var ok = true;
                for (var i = 1; i < phrase.Length && ok; i++)
                    ok = words[position + i] == phrase[i] || PersianText.Stem(words[position + i]) == phrase[i];
                if (ok)
                    return type;
            }
        }
        return null;
    }

    /// <summary>
    /// آیا نام یک دستهٔ موجود همین نوع را نشان می‌دهد؟ مثال: دستهٔ «گوشی موبایل» یا «گوشی» یا نامک «mobile-phone» ← بله برای گوشی؛
    /// دستهٔ «لوازم جانبی گوشی» ← خیر (نوع دسته از اول نامش «لوازم» است، نه گوشی).
    /// </summary>
    public static bool NameMatches(ProductType type, string? categoryName, string? categorySlug)
    {
        if (string.Equals(categorySlug, type.Key, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(PersianText.Normalize(categoryName), PersianText.Normalize(type.Name), StringComparison.Ordinal))
            return true;
        return DetectHead(categoryName) is { } detected && detected.Key == type.Key;
    }
}
