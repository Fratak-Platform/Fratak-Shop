using System.Globalization;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – «خواسته‌های خرید» داخل پیام (لایهٔ ۱، بخش دوم)
//  از جملهٔ مشتری چیزهایی بیرون کشیده می‌شود که «نام کالا» نیستند ولی فهرست کالا را عوض می‌کنند:
//   • بودجه: «زیر ۲ میلیون»، «بین ۵۰۰ تا ۸۰۰ هزار تومن»، «حدود ۳ میلیون»، «۱٫۵ میلیون به پایین»، «بالای ۱۰ میلیون»
//   • مرتب‌سازی: «ارزون‌ترین»، «گرون‌ترین»، «پرفروش‌ترین/محبوب‌ترین»، «جدیدترین»، «بیشترین تخفیف»
//   • فقط تخفیف‌دارها: «هدفون تخفیف‌دار»
//   • کاربرد: «برای گیم»، «واسه دانشجو»، «مخصوص ورزش»، «کادو برای مامانم»
//   • درخواست مشاوره: «بهترین»، «پیشنهاد»، «مناسب»، «به‌صرفه»
//  این واژه‌ها از متن برداشته می‌شوند (Rest) تا جست‌وجو فقط با نام کالا انجام شود. مثال:
//  «بهترین هدفون بلوتوثی زیر ۲ میلیون برای ورزش» ← Rest = «بهترین هدفون بلوتوثی»، MaxPrice = ۲٬۰۰۰٬۰۰۰، Purpose = sport، Advice = true.
//  قاعدهٔ مهم: «پاوربانک ۲۰ هزار» قیمت نیست (ظرفیت میلی‌آمپر است)؛ «هزار» بدون «تومان» یا واژهٔ بودجه هرگز قیمت حساب نمی‌شود.
//  تست: Unit/AssistantRound25Tests.cs. محل استفاده: AssistantUnderstanding.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک کاربرد شناخته‌شده. مثال: sport ← «ورزش، باشگاه، دویدن» و واژه‌های نشانه در مشخصات کالا «ضد آب، ضد تعریق، سبک».
/// </summary>
/// <param name="Key">کلید انگلیسی.</param>
/// <param name="Title">عنوان فارسی برای متن پاسخ («برای ورزش»).</param>
/// <param name="Triggers">واژه‌هایی که بعد از «برای/واسه/مخصوص/جهت» این کاربرد را نشان می‌دهند.</param>
/// <param name="Hints">واژه‌هایی که اگر در نام/مشخصات کالا باشد، کالا برای این کاربرد مناسب‌تر است (امتیاز مشاور).</param>
public sealed record AssistantPurpose(string Key, string Title, string[] Triggers, string[] Hints);

/// <summary>
/// خواسته‌های خرید یک پیام.
/// </summary>
/// <param name="MinPrice">کمترین قیمت (تومان) یا null.</param>
/// <param name="MaxPrice">بیشترین قیمت (تومان) یا null.</param>
/// <param name="Sort">مرتب‌سازی خواسته‌شده یا null.</param>
/// <param name="Discounted">فقط کالاهای تخفیف‌دار.</param>
/// <param name="Purpose">کلید کاربرد (<see cref="AssistantSlotParser.Purposes"/>) یا null.</param>
/// <param name="Advice">مشتری «پیشنهاد/بهترین/مناسب» خواسته (مشاور خرید).</param>
/// <param name="Rest">باقی پیام بعد از برداشتن بودجه/مرتب‌سازی/کاربرد (برای فهم و جست‌وجو).</param>
/// <param name="Approximate">بودجه «حدودی» بود (± ۲۰٪)؛ برای متن پاسخ.</param>
public sealed record AssistantSlots(decimal? MinPrice, decimal? MaxPrice, ProductSortBy? Sort, bool Discounted, string? Purpose, bool Advice, string Rest, bool Approximate = false)
{
    /// <summary>بودجه دارد؟</summary>
    public bool HasBudget => MinPrice is not null || MaxPrice is not null;

    /// <summary>چیزی که فهرست کالا را عوض کند (بودجه، مرتب‌سازی، تخفیف، کاربرد).</summary>
    public bool Strong => HasBudget || Sort is not null || Discounted || Purpose is not null;

    /// <summary>هر خواسته‌ای (حتی فقط «پیشنهاد بده»).</summary>
    public bool Any => Strong || Advice;

    /// <summary>بدون هیچ خواسته‌ای.</summary>
    public static AssistantSlots None(string text) => new(null, null, null, false, null, false, text);
}

/// <summary>بیرون کشیدن خواسته‌های خرید از پیام. <see cref="Extract"/> را ببینید.</summary>
public static partial class AssistantSlotParser
{
    /// <summary>کمترین و بیشترین مبلغ معقول (تومان)؛ بیرون از این بازه عدد قیمت حساب نمی‌شود.</summary>
    public const decimal MinAmount = 1_000, MaxAmount = 10_000_000_000;

    /// <summary>کاربردهای شناخته‌شده.</summary>
    public static readonly AssistantPurpose[] Purposes =
    [
        new("gaming", "بازی و گیمینگ", ["بازی", "گیم", "گیمینگ", "گیمر", "پابجی", "کالاف", "فورتنایت"], ["گیمینگ", "گیم", "تاخیر کم", "لیتنسی", "rgb", "نورپردازی", "بازی"]),
        new("student", "دانشجو و دانش‌آموز", ["دانشجو", "دانشجویی", "دانش آموز", "دانشآموز", "مدرسه", "دانشگاه", "درس", "کلاس", "کلاس آنلاین"], ["سبک", "قابل حمل", "میکروفون", "باتری"]),
        new("photo", "عکاسی و تولید محتوا", ["عکاسی", "عکس", "فیلمبرداری", "ولاگ", "یوتیوب", "اینستاگرام", "تولید محتوا", "لایو"], ["دوربین", "لرزشگیر", "پایه", "نور", "رینگ لایت", "میکروفون", "مگاپیکسل"]),
        new("sport", "ورزش", ["ورزش", "ورزشی", "باشگاه", "دویدن", "دوچرخه", "دوچرخه سواری", "کوهنوردی", "شنا", "پیاده روی"], ["ضد آب", "ضدآب", "ضد تعریق", "تعریق", "ورزشی", "سبک", "ip67", "ip68", "ضربان"]),
        new("music", "موسیقی", ["موزیک", "موسیقی", "آهنگ", "اهنگ"], ["باس", "کیفیت صدا", "hifi", "نویز", "استریو", "درایور"]),
        new("travel", "سفر", ["سفر", "مسافرت", "کمپ", "کمپینگ", "طبیعت گردی"], ["سبک", "قابل حمل", "ظرفیت بالا", "پرظرفیت", "ضد آب", "تاشو"]),
        new("car", "خودرو", ["ماشین", "خودرو", "ماشینم"], ["فندکی", "هولدر", "خودرو", "ماشین", "مگنتی"]),
        new("work", "کار و تماس", ["کار", "اداره", "جلسه", "تماس", "مکالمه", "دفتر", "شرکت"], ["میکروفون", "نویز", "تماس", "مکالمه", "کنسلینگ"]),
        new("kids", "کودک", ["بچه", "کودک", "بچه ها", "نوزاد"], ["کودک", "بچه", "ایمن", "فانتزی"]),
        new("elderly", "سالمندان", ["پدربزرگ", "مادربزرگ", "سالمند", "سالمندان", "مسن"], ["ساده", "صفحه بزرگ", "صدای بلند", "دکمه بزرگ", "فونت بزرگ"]),
        new("gift", "هدیه", ["هدیه", "کادو", "تولد", "سالگرد", "ولنتاین", "مامانم", "بابام", "همسرم", "خانمم", "شوهرم", "دوستم", "دوست دخترم", "دوست پسرم", "خواهرم", "برادرم"], ["جعبه", "شیک", "هدیه", "کادویی"]),
    ];

    private static readonly HashSet<string> PurposeLeads = new(StringComparer.Ordinal) { "برای", "واسه", "واسهی", "مخصوص", "جهت", "بدرد", "درد" };

    private static readonly Dictionary<string, decimal> Units = new(StringComparer.Ordinal)
    {
        ["میلیون"] = 1_000_000m, ["ملیون"] = 1_000_000m, ["ملیونی"] = 1_000_000m, ["میلیونی"] = 1_000_000m, ["میلیونه"] = 1_000_000m, ["تومن"] = 1m, ["م"] = 1_000_000m, ["میلیارد"] = 1_000_000_000m,
        ["هزار"] = 1_000m, ["هزاری"] = 1_000m, ["هزاره"] = 1_000m, ["تومان"] = 1m, ["تومانی"] = 1m, ["تومانه"] = 1m, ["تومنی"] = 1m, ["تومنه"] = 1m, ["ریال"] = 0.1m,
    };

    private static readonly Dictionary<string, decimal> WordNumbers = new(StringComparer.Ordinal)
    {
        ["یک"] = 1, ["یه"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["پنج"] = 5, ["شش"] = 6, ["شیش"] = 6, ["هفت"] = 7, ["هشت"] = 8, ["نه"] = 9, ["ده"] = 10,
        ["یازده"] = 11, ["دوازده"] = 12, ["پانزده"] = 15, ["پونزده"] = 15, ["بیست"] = 20, ["سی"] = 30, ["چهل"] = 40, ["پنجاه"] = 50, ["پنجا"] = 50, ["شصت"] = 60,
        ["هفتاد"] = 70, ["هشتاد"] = 80, ["نود"] = 90, ["صد"] = 100, ["دویست"] = 200, ["سیصد"] = 300, ["چهارصد"] = 400, ["پانصد"] = 500, ["پونصد"] = 500,
        ["ششصد"] = 600, ["هفتصد"] = 700, ["هشتصد"] = 800, ["نهصد"] = 900,
    };

    private static readonly HashSet<string> MaxLeads = new(StringComparer.Ordinal) { "زیر", "کمتر", "حداکثر", "نهایتا", "نهایت", "سقف", "ارزانتر", "ارزونتر", "تا", "زیره" };
    private static readonly HashSet<string> MinLeads = new(StringComparer.Ordinal) { "بالای", "بالاتر", "بیشتر", "حداقل", "گرانتر", "گرونتر", "بالا" };
    private static readonly HashSet<string> ApproxLeads = new(StringComparer.Ordinal) { "حدود", "حدودا", "تقریبا", "حدوده", "حدودی" };
    private static readonly HashSet<string> BudgetWords = new(StringComparer.Ordinal) { "بودجه", "بودجم", "بودجمه", "پول", "پولم" };

    /// <summary>یکاهای اندازه: عددی که بعدش یکی از این‌ها بیاید قیمت نیست («زیر ۱۰ وات»، «بالای ۱۰۰۰۰ میلی آمپر»، «۲۰ هزار میلی آمپر»).</summary>
    private static readonly HashSet<string> Measures = new(StringComparer.Ordinal)
    {
        "وات", "آمپر", "امپر", "میلی", "میلیآمپر", "اینچ", "گیگ", "گیگابایت", "ترابایت", "مگابایت", "مگاپیکسل", "متر", "سانت", "سانتی", "میلیمتر", "هرتز",
        "ساعت", "دقیقه", "ثانیه", "روز", "هفته", "ماه", "سال", "عدد", "تا", "گرم", "کیلو", "ولت", "درصد", "mah", "w", "gb", "tb", "mp", "hz", "v", "پورت", "نفر", "بار",
    };

    /// <summary>واژه‌های فاصل بین واژهٔ بودجه و عدد که نادیده گرفته می‌شوند («بودجه‌ام حدود ۲ میلیونه»).</summary>
    private static readonly HashSet<string> Gap = new(StringComparer.Ordinal) { "من", "ام", "هم", "فقط", "قیمت", "قیمتش", "از", "داره", "دارم", "هست", "که", "در", "حد", "نهایت" };

    private static readonly (string[] Words, ProductSortBy Sort)[] SortCues =
    [
        (["ارزانترین", "ارزونترین", "ارزانترینش", "ارزونترینش", "ارزانترینها", "ارزونترینها", "ارزانترینا", "ارزونترینا", "کمترین قیمت", "ارزان قیمت", "ارزون قیمت", "قیمت پایین", "ارزون", "ارزان", "ارزونش", "ارزونی"], ProductSortBy.Cheapest),
        (["گرانترین", "گرونترین", "گرانترینش", "گرونترینش", "گرانترینها", "گرونترینها", "بیشترین قیمت", "لوکس ترین", "لوکسترین"], ProductSortBy.MostExpensive),
        (["پرفروشترین", "پرفروشترینها", "پرفروشترینش", "پرفروش", "پرفروشها", "پرفروشا", "محبوبترین", "محبوبترینها", "محبوب", "پرطرفدار", "پرطرفدارترین"], ProductSortBy.BestSelling),
        (["جدیدترین", "جدیدترینها", "جدیدترینش", "تازهترین", "جدیدترینا", "مدل جدید", "جدیدا"], ProductSortBy.Newest),
        (["بیشترین تخفیف", "پرتخفیف", "پرتخفیفترین", "پرتخفیفترینها", "تخفیف بیشتر"], ProductSortBy.MostDiscounted),
    ];

    private static readonly string[] DiscountCues = ["تخفیفدار", "تخفیفدارها", "تخفیفدارا", "تخفیف خورده", "با تخفیف", "تخفیفی", "حراجی"];
    private static readonly string[] AdviceCues = ["بهترین", "بهترینش", "بهترینها", "پیشنهاد", "پیشنهادی", "پیشنهادت", "پیشنهادتون", "مناسب", "مناسبه", "بصرفه", "به صرفه", "باکیفیت", "با کیفیت", "کیفیت خوب"];

    /// <summary>واژه‌هایی که فقط برای مشاوره‌اند و از Rest برداشته می‌شوند (بقیهٔ واژه‌های مشاوره در جمله می‌مانند تا «چی پیشنهاد میدی» همان کالایاب بماند).</summary>
    private static readonly HashSet<string> AdviceRemovable = new(StringComparer.Ordinal) { "بصرفه", "به", "صرفه", "باکیفیت", "با", "کیفیت", "خوب" };

    /// <summary>واژه‌هایی که بعد از برداشتن خواسته‌ها بی‌معنا می‌مانند («کالاهای پرفروش» ← «کالاهای»).</summary>
    private static readonly HashSet<string> Leftovers = new(StringComparer.Ordinal) { "کالاهای", "کالاها", "محصولات", "اجناس", "چیزای", "چیزهای", "ها", "های", "هایی", "ترین", "ترینش" };

    /// <summary>همهٔ واژه‌های این فایل (برای اینکه «غلط تایپی» حساب نشوند).</summary>
    public static IReadOnlySet<string> Lexicon => LexiconSet.Value;

    private static readonly Lazy<HashSet<string>> LexiconSet = new(() =>
        Purposes.SelectMany(p => p.Triggers.Concat(p.Hints))
            .Concat(Units.Keys).Concat(WordNumbers.Keys).Concat(MaxLeads).Concat(MinLeads).Concat(ApproxLeads).Concat(BudgetWords).Concat(Gap)
            .Concat(SortCues.SelectMany(c => c.Words)).Concat(DiscountCues).Concat(AdviceCues).Concat(Leftovers).Concat(PurposeLeads)
            .SelectMany(w => w.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length >= 2)
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>کاربرد با کلید. مثال: Purpose("sport").Title ← «ورزش».</summary>
    public static AssistantPurpose? Purpose(string? key) => key is null ? null : Purposes.FirstOrDefault(p => p.Key == key);

    /// <summary>
    /// خواسته‌های خرید یک پیام. مثال‌ها:
    /// «هدفون زیر ۲ میلیون» ← MaxPrice = ۲٬۰۰۰٬۰۰۰، Rest = «هدفون»؛
    /// «گوشی بین ۵ تا ۸ میلیون» ← ۵ تا ۸ میلیون؛ «ساعت هوشمند حدود ۳ میلیون» ← ۲٫۴ تا ۳٫۶ میلیون (Approximate)؛
    /// «ارزون‌ترین پاوربانک» ← Sort = Cheapest؛ «پاوربانک ۲۰ هزار» ← بدون بودجه (Rest همان متن).
    /// </summary>
    public static AssistantSlots Extract(string? text)
    {
        var normalized = PersianText.Normalize(AssistantNlu.Cut(text));
        normalized = DecimalMark().Replace(normalized, "$1.$2");
        normalized = Thousands().Replace(normalized, "$1");
        normalized = GluedUnit().Replace(normalized, "$1 $2");
        // نیم‌فاصله در نرمال‌سازی فاصله می‌شود: «ارزون‌ترین» ← «ارزون ترین» ← دوباره «ارزونترین»؛ «تخفیف‌دار» ← «تخفیفدار».
        normalized = GlueSuffix().Replace(normalized, "$1");
        normalized = normalized.Replace("تخفیف دار", "تخفیفدار", StringComparison.Ordinal).Replace("پر تخفیف", "پرتخفیف", StringComparison.Ordinal)
            .Replace("پر فروش", "پرفروش", StringComparison.Ordinal).Replace("پر طرفدار", "پرطرفدار", StringComparison.Ordinal);
        var cleaned = Spaces().Replace(NonWord().Replace(normalized, " "), " ").Trim();
        if (cleaned.Length == 0)
            return AssistantSlots.None(text ?? string.Empty);
        var tokens = cleaned.Split(' ');
        var removed = new bool[tokens.Length];
        var hasBudgetWord = tokens.Any(t => BudgetWords.Contains(t) || t.StartsWith("بودج", StringComparison.Ordinal));

        decimal? min = null, max = null;
        var approximate = false;
        ParseBudget(tokens, removed, hasBudgetWord, ref min, ref max, ref approximate);

        // مرتب‌سازی (عبارت‌های دوواژه‌ای اول).
        ProductSortBy? sort = null;
        foreach (var (words, cue) in SortCues)
        {
            if (sort is not null)
                break;
            foreach (var word in words.OrderByDescending(w => w.Length))
                if (MarkPhrase(tokens, removed, word))
                {
                    sort = cue;
                    break;
                }
        }

        var purpose = ParsePurpose(tokens, removed);

        // مشاوره: واژه‌ها می‌مانند (به‌جز «به‌صرفه/باکیفیت» که نام کالا نیستند).
        var advice = false;
        foreach (var cue in AdviceCues)
        {
            var parts = cue.Split(' ');
            for (var i = 0; i + parts.Length <= tokens.Length; i++)
            {
                if (!Enumerable.Range(0, parts.Length).All(k => tokens[i + k] == parts[k]))
                    continue;
                advice = true;
                if (parts.All(AdviceRemovable.Contains))
                    for (var k = 0; k < parts.Length; k++)
                        removed[i + k] = true;
            }
        }

        // فقط تخفیف‌دار: وقتی نام کالایی هم در پیام هست؛ وگرنه «تخفیف‌دارها رو نشون بده» همان «تخفیف‌ها» است.
        var discounted = false;
        var probe = (bool[])removed.Clone();
        foreach (var cue in DiscountCues)
            discounted |= MarkPhrase(tokens, probe, cue);
        if (discounted)
        {
            var restWords = AssistantNlu.Words(string.Join(' ', tokens.Where((_, i) => !probe[i] && !Leftovers.Contains(tokens[i]))));
            if (restWords.Count > 0)
                Array.Copy(probe, removed, removed.Length);
            else
                discounted = false;
        }

        var any = min is not null || max is not null || sort is not null || discounted || purpose is not null;
        if (!any && !advice)
            return AssistantSlots.None(text ?? string.Empty);

        var rest = new List<string>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (removed[i])
                continue;
            // «ها» بعد از واژهٔ برداشته‌شده («جدیدترین ها») و «کالاهای» بی‌نام هم برداشته می‌شود.
            if (any && Leftovers.Contains(tokens[i]))
                continue;
            rest.Add(tokens[i]);
        }
        return new AssistantSlots(min, max, sort, discounted, purpose, advice, string.Join(' ', rest), approximate);
    }

    /// <summary>
    /// متن کوتاه بودجه برای پاسخ و دکمه‌ها. مثال: (null، ۲٬۰۰۰٬۰۰۰) ← «زیر ۲ میلیون تومان»؛ (۵۰۰٬۰۰۰، null) ← «بالای ۵۰۰ هزار تومان»؛
    /// (۱٬۰۰۰٬۰۰۰، ۲٬۰۰۰٬۰۰۰) ← «۱ تا ۲ میلیون تومان».
    /// </summary>
    public static string BudgetLabel(decimal? min, decimal? max) => (min, max) switch
    {
        ({ } a, { } b) => $"{ShortNumber(a, b)} تا {AssistantFinder.Short(b)}",
        (null, { } b) => $"زیر {AssistantFinder.Short(b)}",
        ({ } a, null) => $"بالای {AssistantFinder.Short(a)}",
        _ => string.Empty,
    };

    /// <summary>
    /// عدد اول بازه بدون «تومان»؛ اگر هم‌ردهٔ عدد دوم باشد بدون واحد هم.
    /// مثال: (۱م، ۲م) ← «۱»؛ (۸۰۰ هزار، ۱٫۲م) ← «۸۰۰ هزار».
    /// </summary>
    private static string ShortNumber(decimal value, decimal other)
    {
        var text = AssistantFinder.Short(value);
        text = text.EndsWith(" تومان", StringComparison.Ordinal) ? text[..^" تومان".Length] : text;
        if (Tier(value) == Tier(other))
            foreach (var unit in new[] { " میلیون", " هزار" })
                if (text.EndsWith(unit, StringComparison.Ordinal))
                    return text[..^unit.Length];
        return text;
    }

    private static int Tier(decimal value) => value >= 1_000_000 ? 2 : value >= 1_000 ? 1 : 0;

    // ── بودجه ───────────────────────────────────────────────────

    /// <summary>یک مبلغ خوانده‌شده: مقدار به تومان، اندیس بعد از آن، واحد (میلیون/هزار) داشت؟ واحد پول (تومان/ریال) داشت؟ صفت بود («میلیونی»)؟</summary>
    private readonly record struct Amount(decimal Raw, decimal Multiplier, int End, bool HasUnit, bool HasCurrency, bool Adjective, decimal CurrencyFactor)
    {
        /// <summary>مقدار به تومان با قاعدهٔ «بدون واحد»: کمتر از ۱۰۰ ← میلیون؛ ۱۰۰ تا ۹۹٬۹۹۹ ← هزار؛ بیشتر ← خود عدد.</summary>
        public decimal Toman(decimal? inherited = null)
        {
            var multiplier = HasUnit ? Multiplier : inherited ?? (Raw < 100 ? 1_000_000m : Raw < 100_000 ? 1_000m : 1m);
            return Raw * multiplier * CurrencyFactor;
        }
    }

    private static void ParseBudget(string[] tokens, bool[] removed, bool hasBudgetWord, ref decimal? min, ref decimal? max, ref bool approximate)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            if (removed[i])
                continue;
            var token = tokens[i];

            // «بین ۲ و ۳ میلیون» / «بین ۲ تا ۳ میلیون» / «از ۲ تا ۳ میلیون»
            if (token is "بین" or "از" && TryAmount(tokens, i + 1, out var a, rangeStart: true) && a.End < tokens.Length && tokens[a.End] is "تا" or "و"
                && TryAmount(tokens, a.End + 1, out var b) && (token == "بین" || tokens[a.End] == "تا"))
            {
                var inherited = !a.HasUnit && b.HasUnit ? b.Multiplier : (decimal?)null;
                // «بین ۵ تا ۸» بی‌واحد هم قیمت است (میلیون)؛ ولی «بین ۱۰ تا ۲۰ هزار» بی‌تومان ظرفیت است نه قیمت.
                var trusted = Pricey(a) || Pricey(b) || hasBudgetWord || (token == "بین" && !a.HasUnit && !b.HasUnit);
                if (Valid(a.Toman(inherited)) && Valid(b.Toman()) && trusted)
                {
                    var (low, high) = Order(a.Toman(inherited), b.Toman());
                    (min, max) = (low, high);
                    Mark(removed, i, b.End);
                    return;
                }
            }

            // «۲ تا ۳ میلیون» (بدون «بین/از»؛ عدد دوم باید واحد داشته باشد تا با «۲ تا هدفون» قاطی نشود)
            if (IsDigits(token) && TryAmount(tokens, i, out var first, rangeStart: true) && !first.HasUnit && first.End < tokens.Length && tokens[first.End] == "تا"
                && TryAmount(tokens, first.End + 1, out var second) && (Pricey(second) || hasBudgetWord && second.HasUnit))
            {
                var low = first.Toman(second.HasUnit ? second.Multiplier : null);
                if (Valid(low) && Valid(second.Toman()))
                {
                    (min, max) = Order(low, second.Toman());
                    Mark(removed, i, second.End);
                    return;
                }
            }

            // «زیر/کمتر از/حداکثر/سقف/تا ۲ میلیون»، «بودجه‌ام ۲ میلیونه»، «حدود ۲ میلیون»، «بالای ۵۰۰ هزار»
            var isMax = MaxLeads.Contains(token);
            var isMin = MinLeads.Contains(token) && token != "بالا";
            var isApprox = ApproxLeads.Contains(token) || token == "حول"
                || (token == "در" && i + 1 < tokens.Length && tokens[i + 1] is "حد" or "حدود")
                || (token == "دور" && i + 2 < tokens.Length && tokens[i + 1] == "و" && tokens[i + 2] == "بر");
            var isBudget = BudgetWords.Contains(token) || token.StartsWith("بودج", StringComparison.Ordinal) || token == "سقف";
            if (isMax || isMin || isApprox || isBudget)
            {
                var j = i + 1;
                var approxInside = isApprox;
                // فاصل‌ها: «کمتر از»، «بودجه من حدود»، «در حد»، «حول و حوش».
                while (j < tokens.Length && j - i <= 3 && (Gap.Contains(tokens[j]) || ApproxLeads.Contains(tokens[j]) || tokens[j] is "و" or "حوش" or "حدود" or "بر"))
                {
                    if (ApproxLeads.Contains(tokens[j]) || tokens[j] is "حدود" or "حد")
                        approxInside = true;
                    j++;
                }
                if (TryAmount(tokens, j, out var amount))
                {
                    var trusted = amount.HasUnit || amount.HasCurrency || hasBudgetWord || isBudget || isApprox || token is "زیر" or "بالای" or "حداکثر" or "حداقل" or "سقف" or "کمتر" or "بیشتر";
                    // «تا ۲۰» بی‌واحد معمولاً تعداد است؛ «تا» فقط با واحد یا واژهٔ بودجه.
                    if (token == "تا" && !(amount.HasUnit || amount.HasCurrency || hasBudgetWord))
                        trusted = false;
                    var value = amount.Toman();
                    if (trusted && Valid(value))
                    {
                        if (approxInside || isApprox || amount.Adjective && !isMax && !isMin)
                        {
                            (min, max) = (Math.Round(value * 0.8m), Math.Round(value * 1.2m));
                            approximate = true;
                        }
                        else if (isMin)
                            min = value;
                        else
                            max = value;
                        Mark(removed, i, amount.End);
                        TrailingDirection(tokens, removed, amount.End, value, ref min, ref max);
                        return;
                    }
                }
            }

            // «۲ میلیون به پایین»، «۵۰۰ هزار تومن به بالا»، یا مبلغ تنها با واحد («گوشی ۵ میلیونی»، «هدفون ۸۰۰ تومنی»)
            if (IsDigits(token) || WordNumbers.ContainsKey(token))
            {
                if (!TryAmount(tokens, i, out var bare))
                    continue;
                var value = bare.Toman();
                var direction = Direction(tokens, bare.End);
                var priced = Pricey(bare) || bare.HasUnit && hasBudgetWord;
                if (direction != 0 && (bare.HasUnit || bare.HasCurrency || hasBudgetWord) && Valid(value))
                {
                    if (direction < 0)
                        max = value;
                    else
                        min = value;
                    // «از ۲ میلیون به بالا»: «از» قبل از مبلغ هم برداشته می‌شود.
                    Mark(removed, i > 0 && tokens[i - 1] == "از" ? i - 1 : i, bare.End + 2);
                    return;
                }
                if (priced && Valid(value))
                {
                    (min, max) = (Math.Round(value * 0.8m), Math.Round(value * 1.2m));
                    approximate = true;
                    Mark(removed, i, bare.End);
                    return;
                }
            }
        }
    }

    /// <summary>بعد از مبلغ: «به پایین/یا کمتر» ← ‎-1؛ «به بالا/یا بیشتر» ← ‎+1؛ وگرنه ۰.</summary>
    private static int Direction(string[] tokens, int at)
    {
        if (at + 1 < tokens.Length && tokens[at] is "به" or "یا" or "و")
        {
            if (tokens[at + 1] is "پایین" or "پایینتر" or "کمتر" or "کم")
                return -1;
            if (tokens[at + 1] is "بالا" or "بالاتر" or "بیشتر")
                return 1;
        }
        return 0;
    }

    /// <summary>«زیر ۲ میلیون به پایین» یا «از ۵۰۰ به بالا»: واژه‌های جهت بعد از مبلغ هم برداشته می‌شوند.</summary>
    private static void TrailingDirection(string[] tokens, bool[] removed, int at, decimal value, ref decimal? min, ref decimal? max)
    {
        var direction = Direction(tokens, at);
        if (direction == 0)
            return;
        removed[at] = true;
        removed[at + 1] = true;
        if (direction > 0 && max == value && min is null)
        {
            min = value;
            max = null;
        }
    }

    /// <summary>
    /// خواندن مبلغ از اندیس i: عدد (رقمی یا حروفی + «و نیم»)، واحد اختیاری، «و» + عدد دوم (مثل «۲ میلیون و ۵۰۰ هزار»)، تومان/ریال اختیاری.
    /// عدد حروفی («دو»، «پونصد») فقط وقتی واحد دارد پذیرفته می‌شود تا «یه هدفون» عدد حساب نشود.
    /// </summary>
    private static bool TryAmount(string[] tokens, int i, out Amount amount, bool rangeStart = false)
    {
        amount = default;
        if (i >= tokens.Length)
            return false;
        var at = i;
        decimal raw;
        var wordNumber = false;
        if (IsDigits(tokens[at]) && decimal.TryParse(tokens[at], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out raw))
            at++;
        else if (WordNumbers.TryGetValue(tokens[at], out raw))
        {
            wordNumber = true;
            at++;
            // «دویست و پنجاه»
            while (at + 1 < tokens.Length && tokens[at] == "و" && WordNumbers.TryGetValue(tokens[at + 1], out var more) && more < raw)
            {
                raw += more;
                at += 2;
            }
        }
        else
            return false;
        // «دو و نیم» / «۲ و نیم»
        if (at + 1 < tokens.Length && tokens[at] == "و" && tokens[at + 1] == "نیم")
        {
            raw += 0.5m;
            at += 2;
        }

        decimal multiplier = 1;
        var hasUnit = false;
        var adjective = false;
        decimal currency = 1;
        var hasCurrency = false;
        if (at < tokens.Length && Units.TryGetValue(tokens[at], out var unit))
        {
            if (unit >= 1_000)
            {
                // «م» فقط بلافاصله بعد از عدد رقمی («۲ م»)؛ «م» تنها معنای دیگری دارد.
                if (tokens[at] != "م" || !wordNumber)
                {
                    multiplier = unit;
                    hasUnit = true;
                    adjective = tokens[at].EndsWith('ی');
                    at++;
                }
            }
            else
            {
                currency = unit;
                hasCurrency = true;
                adjective = tokens[at].EndsWith('ی');
                at++;
            }
        }
        // «۲ میلیون و ۵۰۰ (هزار)»
        if (hasUnit && at + 1 < tokens.Length && tokens[at] == "و" && IsDigits(tokens[at + 1])
            && decimal.TryParse(tokens[at + 1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var extra))
        {
            var next = at + 2;
            var extraMultiplier = multiplier >= 1_000_000 ? 1_000m : 1m;
            if (next < tokens.Length && Units.TryGetValue(tokens[next], out var extraUnit) && extraUnit >= 1_000 && extraUnit < multiplier)
            {
                extraMultiplier = extraUnit;
                next++;
            }
            raw += extra * extraMultiplier / multiplier;
            at = next;
        }
        if (!hasCurrency && at < tokens.Length && Units.TryGetValue(tokens[at], out var cur) && cur < 1_000)
        {
            currency = cur;
            hasCurrency = true;
            adjective |= tokens[at].EndsWith('ی');
            at++;
        }
        if (wordNumber && !hasUnit && !hasCurrency)
            return false;
        // «۲۰ هزار میلی آمپر»، «۱۰ وات»: اندازه است نه قیمت.
        // دور ۲۵: «تا» بعد از عدد اول بازه («بین ۵ تا ۸ میلیون») شمارنده نیست، رابط بازه است (rangeStart)؛
        // ولی «۲ تا هدفون» همچنان تعداد است چون بعد از «تا» مبلغی نمی‌آید.
        if (!hasCurrency && at < tokens.Length && Measures.Contains(tokens[at]) && !(rangeStart && tokens[at] == "تا"))
            return false;
        amount = new Amount(raw, multiplier, at, hasUnit, hasCurrency, adjective, currency);
        return true;
    }

    /// <summary>
    /// مبلغی که خودش (بی واژهٔ بودجه) قیمت است: تومان/ریال دارد، یا «میلیون»، یا «هزار» با عدد ≥ ۱۰۰ («۸۰۰ هزار»).
    /// «۲۰ هزار» تنها قیمت نیست (معمولاً ظرفیت میلی‌آمپر پاوربانک است).
    /// </summary>
    private static bool Pricey(Amount a) => a.HasCurrency || a.HasUnit && (a.Multiplier >= 1_000_000 || a.Raw >= 100);

    private static bool Valid(decimal value) => value is >= MinAmount and <= MaxAmount;

    private static (decimal Low, decimal High) Order(decimal a, decimal b) => a <= b ? (a, b) : (b, a);

    private static bool IsDigits(string token) => token.Length > 0 && char.IsAsciiDigit(token[0]) && token.All(c => char.IsAsciiDigit(c) || c == '.');

    private static void Mark(bool[] removed, int from, int toExclusive)
    {
        for (var k = from; k < Math.Min(toExclusive, removed.Length); k++)
            removed[k] = true;
    }

    // ── کاربرد ───────────────────────────────────────────────────

    /// <summary>
    /// «برای/واسه/مخصوص/جهت + واژهٔ کاربرد» (تا دو واژه بعد). «هدیه/کادو» بدون «برای» هم کاربرد هدیه است.
    /// مثال: «هدفون برای بازی» ← gaming؛ «یه کادو برای مامانم» ← gift.
    /// </summary>
    private static string? ParsePurpose(string[] tokens, bool[] removed)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            if (removed[i] || !PurposeLeads.Contains(tokens[i]))
                continue;
            var lead = i;
            var j = i + 1;
            if (tokens[i] == "درد" && (i == 0 || tokens[i - 1] != "به"))
                continue;
            if (j < tokens.Length && tokens[j] is "ی" or "یه" or "یک")
                j++;
            for (var k = j; k < Math.Min(tokens.Length, j + 2); k++)
            {
                foreach (var purpose in Purposes)
                    foreach (var trigger in purpose.Triggers.OrderByDescending(t => t.Length))
                    {
                        var parts = trigger.Split(' ');
                        if (k + parts.Length > tokens.Length || !Enumerable.Range(0, parts.Length).All(p => Matches(tokens[k + p], parts[p])))
                            continue;
                        Mark(removed, lead, k + parts.Length);
                        if (lead > 0 && tokens[lead] == "درد" && tokens[lead - 1] == "به")
                            removed[lead - 1] = true;
                        return purpose.Key;
                    }
            }
        }
        // «کادو»، «هدیه» تنها (بدون «برای»).
        for (var i = 0; i < tokens.Length; i++)
            if (!removed[i] && tokens[i] is "هدیه" or "کادو" or "کادویی" && (i == 0 || tokens[i - 1] != "کارت"))
            {
                removed[i] = true;
                return "gift";
            }
        return null;
    }

    /// <summary>واژه با پسوند کوتاه: «بازی» ↔ «بازیا/بازیها»؛ واژهٔ ≤۳ حرفی فقط کامل.</summary>
    private static bool Matches(string token, string word) =>
        token == word || word.Length >= 4 && token.StartsWith(word, StringComparison.Ordinal) && token.Length <= word.Length + 2;

    /// <summary>علامت زدن یک عبارت (یک یا چند واژه) اگر در پیام بود. مثال: «کمترین قیمت».</summary>
    private static bool MarkPhrase(string[] tokens, bool[] removed, string phrase)
    {
        var parts = phrase.Split(' ');
        for (var i = 0; i + parts.Length <= tokens.Length; i++)
        {
            if (!Enumerable.Range(0, parts.Length).All(k => !removed[i + k] && tokens[i + k] == parts[k]))
                continue;
            Mark(removed, i, i + parts.Length);
            return true;
        }
        return false;
    }

    [GeneratedRegex(@"(\d)[٫/](\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalMark();

    [GeneratedRegex(@"(\d)[,٬](?=\d{3}(?!\d))", RegexOptions.CultureInvariant)]
    private static partial Regex Thousands();

    [GeneratedRegex(@"(\d)(میلیون|ملیون|هزار|تومان|تومن|ریال|م(?!\p{L}))", RegexOptions.CultureInvariant)]
    private static partial Regex GluedUnit();

    [GeneratedRegex(@"[^\p{L}\p{N}\s.]|(?<!\d)\.|\.(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NonWord();

    [GeneratedRegex(@"(?<=\p{L}) (ترینها|ترینش|ترینا|ترین|تر)(?!\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex GlueSuffix();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
