using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۵ – «پیش‌پردازش زبان» دستیار (لایهٔ ۱)
//  پیش از فهم پیام (AssistantNlu.Parse) متن مشتری تمیز می‌شود تا نوشتن غیررسمی، فینگلیش و غلط تایپی باعث نفهمیدن نشود:
//   ۱. کیبورد اشتباه: «sghl» (سلام با کیبورد انگلیسی) ← «سلام».
//   ۲. فینگلیش: «gheymat powerbank chande» ← «قیمت پاوربانک چنده» (فقط واژه‌های واژه‌نامه؛ نام برندها دست نمی‌خورد).
//   ۳. کشیدهٔ حروف: «سلاااام» ← «سلام».
//   ۴. محاوره و چسباندن: «میخام» ← «میخوام»، «چقد» ← «چقدر»، «تومن» ← «تومان»، «پاور بانک» ← «پاوربانک».
//   ۵. غلط تایپی: فقط واژه‌ای که نه دستیار می‌شناسد نه در نام کالاهاست، و فقط اگر یک واژهٔ آشنای خیلی نزدیک (یک یا دو حرف فرق) باشد.
//      مثال: «باطری» ← «باتری»، «هندزفیری» ← «هندزفری».
//  همه‌چیز خالص و بدون پایگاه داده است (تست: Unit/AssistantRound25Tests.cs). محل استفاده: AssistantUnderstanding.Understand.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// متن آمادهٔ فهم. اگر چیزی عوض نشد Text همان Original است و Fixes خالی.
/// مثال: «قیمت باطری چقد» ← Text = «قیمت باتری چقدر»، Fixes = [«چقد→چقدر»، «باطری→باتری»].
/// </summary>
/// <param name="Original">متن اصلی مشتری (بریده‌شده به ۳۰۰ نویسه).</param>
/// <param name="Text">متن اصلاح‌شده برای فهم.</param>
/// <param name="Fixes">فهرست اصلاح‌ها (برای گزارش و تست). مثال: «layout»، «finglish»، «باطری→باتری».</param>
public sealed record AssistantPrepared(string Original, string Text, IReadOnlyList<string> Fixes)
{
    /// <summary>متن عوض شد؟</summary>
    public bool Changed => Fixes.Count > 0;
}

/// <summary>
/// واژه‌های «آشنا»: واژه‌نامهٔ دستیار + واژه‌های نام کالاها، دسته‌ها و برندهای همین فروشگاه.
/// <para>واژهٔ آشنا هرگز «غلط» حساب نمی‌شود و غلط‌ها فقط به واژهٔ آشنا اصلاح می‌شوند. نسخهٔ فروشگاه هر ۱۰ دقیقه از نو ساخته می‌شود (AssistantService).</para>
/// </summary>
public sealed partial class AssistantVocabulary
{
    /// <summary>بیشترین تعداد واژهٔ کاتالوگ (برای حافظه).</summary>
    public const int MaxCatalogWords = 40_000;

    /// <summary>فقط واژه‌نامهٔ دستیار (بدون کاتالوگ)؛ برای تست و وقتی کاتالوگ در دسترس نیست.</summary>
    public static AssistantVocabulary Base { get; } = new([]);

    private readonly HashSet<string> _words;
    private readonly Dictionary<char, string[]> _byFirst;

    /// <summary>ساخت از واژه‌های کاتالوگ (نام کالا/دسته/برند؛ هر متنی، خودش واژه‌ها را جدا می‌کند).</summary>
    public AssistantVocabulary(IEnumerable<string> catalogTexts)
    {
        var catalog = catalogTexts.SelectMany(Tokens).Where(t => t.Length >= 3).Distinct(StringComparer.Ordinal).Take(MaxCatalogWords).ToList();
        HasCatalog = catalog.Count > 0;
        // همهٔ واژه‌ها با همان نرمال‌سازی پیام مشتری ذخیره می‌شوند (مثلاً «شیائومی» ← «شیایومی»)؛
        // وگرنه واژهٔ درست پس از نرمال‌سازی «ناآشنا» دیده و به اشتباه «اصلاح» می‌شد.
        _words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in AssistantNlu.Lexicon.Concat(AssistantSlotParser.Lexicon).Concat(AssistantLanguage.CommonWords))
        {
            _words.Add(word);
            _words.Add(PersianText.Normalize(word));
        }
        _words.UnionWith(catalog);
        _byFirst = _words.Where(w => w.Length >= 3).GroupBy(w => w[0]).ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>واژه‌های کاتالوگ هم دارد؟ (نسخهٔ Base ندارد.)</summary>
    public bool HasCatalog { get; }

    /// <summary>تعداد کل واژه‌ها.</summary>
    public int Count => _words.Count;

    /// <summary>واژهٔ آشناست؟ مثال: «باتری» ← true؛ «باطری» ← false.</summary>
    public bool Contains(string word) => _words.Contains(word);

    /// <summary>
    /// واژهٔ آشنا با پسوند؟ («باتریش»، «سفارشمو»، «گوشیا»): یک واژهٔ آشنای ≥۳ حرفی + حداکثر ۳ حرف پسوند.
    /// </summary>
    public bool IsSuffixed(string word)
    {
        for (var cut = 1; cut <= 3 && word.Length - cut >= 3; cut++)
            if (_words.Contains(word[..^cut]))
                return true;
        return false;
    }

    /// <summary>واژه‌های آشنایی که با این حرف شروع می‌شوند (برای پیدا کردن نزدیک‌ترین واژه).</summary>
    public IReadOnlyList<string> StartingWith(char first) => _byFirst.TryGetValue(first, out var list) ? list : Array.Empty<string>();

    /// <summary>واژه‌های حرفی یک متن (نرمال‌شده، بدون رقم و نماد). مثال: «پاوربانک ۲۰ هزار!» ← [«پاوربانک»، «هزار»].</summary>
    public static IEnumerable<string> Tokens(string? text) =>
        Letters().Matches(PersianText.Normalize(text)).Select(m => m.Value).Where(t => t.Length >= 2);

    [GeneratedRegex(@"\p{L}+", RegexOptions.CultureInvariant)]
    private static partial Regex Letters();
}

/// <summary>پیش‌پردازش متن مشتری پیش از فهم. <see cref="Prepare"/> را ببینید.</summary>
public static partial class AssistantLanguage
{
    /// <summary>
    /// واژه‌های رایج گفت‌وگو که در فهرست‌های دستیار نیستند ولی درست‌اند (تا «اصلاح» نشوند). مثال: «نرسیده»، «فرستادید»، «خریدم».
    /// </summary>
    public static readonly IReadOnlySet<string> CommonWords = new HashSet<string>(StringComparer.Ordinal)
    {
        "نرسیده", "نرسید", "نیومده", "نیامده", "رسید", "رسیده", "میرسه", "برسه", "بفرستید", "فرستادید", "فرستادین", "ارسال", "ارسالش", "کردم", "کنم", "کنید",
        "کنین", "بکنم", "بدید", "میدید", "میدین", "دادم", "دادید", "خریدم", "خریده", "گرفتم", "گرفته", "شده", "نشده", "هست", "نیست", "نداره", "نمیشه", "بشه",
        "باشه", "بود", "نبود", "دارم", "ندارم", "میخوام", "نمیخوام", "بخوام", "میخواستم", "خواستم", "لطفا", "سلام", "ممنون", "مرسی", "خداحافظ", "سپاس",
        "امروز", "فردا", "دیروز", "هفته", "روز", "ساعت", "الان", "هنوز", "دیگه", "دیگر", "چرا", "کجا", "کجاست", "کی", "چطوری", "چجوری", "چگونه", "خیلی",
        "زیاد", "کم", "بیشتر", "کمتر", "بزرگ", "کوچک", "کوچیک", "سبک", "سنگین", "قوی", "ضعیف", "سریع", "کند", "نو", "جدید", "دست", "دوم", "اصلی",
        "مشکی", "سفید", "قرمز", "آبی", "ابی", "سبز", "زرد", "صورتی", "طلایی", "نقره", "نقره ای", "خاکستری", "بنفش", "نارنجی", "سرمه", "رنگی",
        "بچه", "کودک", "مادر", "پدر", "مامان", "بابا", "همسر", "دوست", "دوستم", "خانواده", "تولد", "هدیه", "کادو", "سفر", "مسافرت", "ماشین", "خونه",
        "خانه", "دفتر", "اداره", "مدرسه", "دانشگاه", "باشگاه", "ورزش", "بازی", "موزیک", "موسیقی", "فیلم", "عکس", "کار", "درس", "تماس", "جلسه",
        "تومان", "میلیون", "هزار", "ریال", "قیمت", "بودجه", "سقف", "حدود", "زیر", "بالای", "بین", "حداکثر", "حداقل", "تقریبا", "حدودا",
        "سفارش", "سفارشم", "خرید", "خریدم", "فاکتور", "صورتحساب", "مرجوع", "مرجوعی", "لغو", "کنسل", "انصراف", "منصرف", "تعویض", "عودت",
        "شیائومی", "سامسونگ", "اپل", "هواوی", "سونی", "انکر", "باسئوس", "لنوو", "ایسوس", "شارژر", "پاوربانک", "هندزفری", "هدفون", "هدست",
        "ایرپاد", "اسپیکر", "کابل", "گوشی", "موبایل", "تبلت", "لپتاپ", "ساعت", "هوشمند", "مچبند", "قاب", "گلس", "محافظ", "کیف", "فلش", "مموری",
        "رم", "هارد", "ماوس", "موس", "کیبورد", "مانیتور", "دوربین", "پایه", "رینگ", "لایت", "چراغ", "لامپ", "باتری", "شارژ", "آداپتور", "اداپتور",
    };

    // ── ۱. کیبورد اشتباه (چیدمان استاندارد فارسی روی دکمه‌های انگلیسی) ──
    private static readonly Dictionary<char, char> Layout = new()
    {
        ['q'] = 'ض', ['w'] = 'ص', ['e'] = 'ث', ['r'] = 'ق', ['t'] = 'ف', ['y'] = 'غ', ['u'] = 'ع', ['i'] = 'ه', ['o'] = 'خ', ['p'] = 'ح', ['['] = 'ج', [']'] = 'چ',
        ['a'] = 'ش', ['s'] = 'س', ['d'] = 'ی', ['f'] = 'ب', ['g'] = 'ل', ['h'] = 'ا', ['j'] = 'ت', ['k'] = 'ن', ['l'] = 'م', [';'] = 'ک', ['\''] = 'گ',
        ['z'] = 'ظ', ['x'] = 'ط', ['c'] = 'ز', ['v'] = 'ر', ['b'] = 'ذ', ['n'] = 'د', ['m'] = 'پ', [','] = 'و', ['H'] = 'آ', ['C'] = 'ژ',
    };

    // ── ۲. فینگلیش (فقط واژه‌های رایج خرید؛ نام برند و مدل انگلیسی می‌ماند) ──
    private static readonly Dictionary<string, string> Finglish = new(StringComparer.Ordinal)
    {
        ["mikham"] = "میخوام", ["mikhastam"] = "میخواستم", ["mikhay"] = "میخوای", ["darid"] = "دارید", ["dari"] = "داری", ["darin"] = "دارین", ["darim"] = "داریم",
        ["gheymat"] = "قیمت", ["ghimat"] = "قیمت", ["gheimat"] = "قیمت", ["gheymatesh"] = "قیمتش", ["ghimatesh"] = "قیمتش", ["chande"] = "چنده", ["chand"] = "چند",
        ["cheghadr"] = "چقدر", ["cheghad"] = "چقدر", ["cheghadre"] = "چقدره", ["chiye"] = "چیه", ["chie"] = "چیه", ["chi"] = "چی",
        ["sabad"] = "سبد", ["sabadam"] = "سبدم", ["kharid"] = "خرید", ["kharidam"] = "خریدم", ["sefaresh"] = "سفارش", ["sefaresham"] = "سفارشم", ["sefareshamo"] = "سفارشمو",
        ["peygiri"] = "پیگیری", ["paygiri"] = "پیگیری", ["ersal"] = "ارسال", ["tahvil"] = "تحویل", ["takhfif"] = "تخفیف", ["takhfifi"] = "تخفیفی",
        ["arzoon"] = "ارزون", ["arzon"] = "ارزون", ["arzan"] = "ارزان", ["arzoontarin"] = "ارزونترین", ["arzantarin"] = "ارزانترین", ["behtarin"] = "بهترین",
        ["mojood"] = "موجود", ["mojod"] = "موجود", ["mojoode"] = "موجوده", ["mojode"] = "موجوده", ["garanti"] = "گارانتی", ["zemanat"] = "ضمانت",
        ["batri"] = "باتری", ["baatri"] = "باتری", ["sharj"] = "شارژ", ["sharjer"] = "شارژر", ["hedfon"] = "هدفون", ["headphone"] = "هدفون",
        ["hendsfree"] = "هندزفری", ["handsfree"] = "هندزفری", ["hendzfree"] = "هندزفری", ["powerbank"] = "پاوربانک", ["paverbank"] = "پاوربانک",
        ["gooshi"] = "گوشی", ["goshi"] = "گوشی", ["mobile"] = "موبایل", ["kabl"] = "کابل", ["kabel"] = "کابل", ["cable"] = "کابل", ["saat"] = "ساعت",
        ["hooshmand"] = "هوشمند", ["hoshmand"] = "هوشمند", ["airpod"] = "ایرپاد", ["airpods"] = "ایرپاد", ["speaker"] = "اسپیکر", ["espiker"] = "اسپیکر",
        ["moghayese"] = "مقایسه", ["fargh"] = "فرق", ["farq"] = "فرق", ["laghv"] = "لغو", ["lagv"] = "لغو", ["cancel"] = "کنسل", ["marjoo"] = "مرجوع",
        ["marjooi"] = "مرجوعی", ["factor"] = "فاکتور", ["faktor"] = "فاکتور", ["zire"] = "زیر", ["zir"] = "زیر", ["balaye"] = "بالای", ["million"] = "میلیون",
        ["milion"] = "میلیون", ["melyon"] = "میلیون", ["toman"] = "تومان", ["tomen"] = "تومن", ["hezar"] = "هزار", ["baraye"] = "برای", ["vase"] = "واسه",
        ["dige"] = "دیگه", ["ham"] = "هم", ["in"] = "این", ["ino"] = "اینو", ["oon"] = "اون", ["ke"] = "که", ["ro"] = "رو", ["be"] = "به", ["ba"] = "با",
        ["ya"] = "یا", ["va"] = "و", ["az"] = "از", ["ta"] = "تا", ["man"] = "من", ["mamnoon"] = "ممنون", ["mamnun"] = "ممنون", ["merci"] = "مرسی",
        ["mersi"] = "مرسی", ["kojast"] = "کجاست", ["koja"] = "کجا", ["kei"] = "کی", ["mirese"] = "میرسه", ["nareside"] = "نرسیده", ["nayomade"] = "نیومده",
        ["hanooz"] = "هنوز", ["hanoz"] = "هنوز", ["ezafe"] = "اضافه", ["kon"] = "کن", ["bede"] = "بده", ["neshoon"] = "نشون", ["neshon"] = "نشون",
        ["pishnahad"] = "پیشنهاد", ["midi"] = "میدی", ["komak"] = "کمک", ["komakam"] = "کمکم", ["rahnama"] = "راهنما", ["poshtibani"] = "پشتیبانی",
        ["operator"] = "اپراتور", ["karshenas"] = "کارشناس", ["vorood"] = "ورود", ["vorod"] = "ورود", ["sabtenam"] = "ثبتنام", ["ramz"] = "رمز",
        ["gaming"] = "گیمینگ", ["bazi"] = "بازی", ["daneshjoo"] = "دانشجو", ["hedye"] = "هدیه", ["kado"] = "کادو", ["varzesh"] = "ورزش", ["safar"] = "سفر",
        ["mosaferat"] = "مسافرت", ["bache"] = "بچه", ["keyfiat"] = "کیفیت", ["keyfiyat"] = "کیفیت", ["kifiat"] = "کیفیت", ["seda"] = "صدا", ["sedash"] = "صداش",
        ["nazarat"] = "نظرات", ["rang"] = "رنگ", ["vazn"] = "وزن", ["khoobe"] = "خوبه", ["khobe"] = "خوبه", ["khoob"] = "خوب", ["asl"] = "اصل", ["asle"] = "اصله",
        ["salamat"] = "سلامت", ["chetori"] = "چطوری", ["mishe"] = "میشه", ["nemishe"] = "نمیشه", ["bekhar"] = "بخر", ["bekharam"] = "بخرم",
    };

    // ── ۴. محاوره ← شکل آشنا (واژهٔ کامل) ──
    private static readonly Dictionary<string, string> Colloquial = new(StringComparer.Ordinal)
    {
        ["میخام"] = "میخوام", ["نمیخام"] = "نمیخوام", ["بخام"] = "بخوام", ["میخاستم"] = "میخواستم", ["میخواسم"] = "میخواستم", ["خاستم"] = "خواستم",
        ["چقد"] = "چقدر", ["چقده"] = "چقدره", ["چن"] = "چند", ["چنتا"] = "چندتا", ["تومن"] = "تومان", ["تومنی"] = "تومانی", ["تومنه"] = "تومانه",
        ["ملیون"] = "میلیون", ["ملیونی"] = "میلیونی", ["ملیونه"] = "میلیونه", ["باطری"] = "باتری", ["باطریش"] = "باتریش", ["اوریجینال"] = "اورجینال",
        ["هنزفری"] = "هندزفری", ["هندسفری"] = "هندزفری", ["هندزفیری"] = "هندزفری", ["پاوربانگ"] = "پاوربانک", ["ایرپادز"] = "ایرپاد", ["مبایل"] = "موبایل",
        ["سفارشو"] = "سفارش رو", ["خریدمو"] = "خریدم رو", ["فاکتورمو"] = "فاکتورم رو", ["کنسلش"] = "کنسل", ["گوشیمو"] = "گوشیم رو",
    };

    // ── ۴ب. دو بخشی ← یک واژه (فاصلهٔ مجازی به فاصله تبدیل شده بود یا جدا نوشته شده) ──
    private static readonly (string From, string To, bool Product)[] Joins =
    [
        ("پاور بانک", "پاوربانک", true), ("هندز فری", "هندزفری", true), ("هند زفری", "هندزفری", true), ("هند فری", "هندزفری", true),
        ("ایر پاد", "ایرپاد", true), ("هد فون", "هدفون", true), ("هد ست", "هدست", true), ("لپ تاپ", "لپتاپ", true),
        ("ارزان ترین", "ارزانترین", false), ("ارزون ترین", "ارزونترین", false), ("گران ترین", "گرانترین", false), ("گرون ترین", "گرونترین", false),
        ("پر فروش", "پرفروش", false), ("پرفروش ترین", "پرفروشترین", false), ("محبوب ترین", "محبوبترین", false), ("جدید ترین", "جدیدترین", false),
        ("تازه ترین", "تازهترین", false), ("ارزان تر", "ارزانتر", false), ("ارزون تر", "ارزونتر", false), ("گران تر", "گرانتر", false),
        ("گرون تر", "گرونتر", false), ("بهترین ها", "بهترینها", false), ("تخفیف دار", "تخفیفدار", false), ("پر تخفیف", "پرتخفیف", false),
    ];

    private static readonly (Regex Pattern, string From, string To, bool Product)[] JoinPatterns = Joins
        .Select(j => (new Regex(@"(?<!\p{L})" + Regex.Escape(j.From) + @"(?!\p{L})", RegexOptions.CultureInvariant), j.From, j.To, j.Product))
        .ToArray();

    /// <summary>گروه حروفی که در تایپ فارسی زیاد جابه‌جا می‌شوند (برای واژه‌های کوتاه فقط همین جابه‌جایی‌ها «غلط تایپی» حساب می‌شود).</summary>
    private static readonly string[] Confusable = ["تط", "سصث", "زذضظ", "قغ", "هح", "اآع", "کگ", "یئ", "وؤ"];

    /// <summary>حروفی که جا انداختن یا اضافه نوشتنشان رایج است («موبایل/مبایل»، «پاوربانک/پاوربانکک»).</summary>
    private const string SoftLetters = "اویه";

    /// <summary>
    /// آماده کردن پیام برای فهم. <paramref name="vocabulary"/> = واژه‌های آشنا (null ← فقط واژه‌نامهٔ دستیار).
    /// <para>مثال‌ها: «sghl» ← «سلام»؛ «gheymat powerbank chande» ← «قیمت پاوربانک چنده»؛ «سلاااام» ← «سلام»؛ «باطری» ← «باتری»؛
    /// «پاوربانک شیائومی» ← بدون تغییر (Text همان Original).</para>
    /// </summary>
    public static AssistantPrepared Prepare(string? text, AssistantVocabulary? vocabulary = null)
    {
        var original = AssistantNlu.Cut(text);
        if (original.Length == 0)
            return new AssistantPrepared(original, original, []);
        var vocab = vocabulary ?? AssistantVocabulary.Base;
        var fixes = new List<string>();
        var value = PersianText.Normalize(original);

        // ۱. کیبورد اشتباه: همهٔ حروف انگلیسی‌اند، شمارهٔ سفارش ندارد و بیشتر واژه‌های تبدیل‌شده آشنا هستند.
        if (!PersianLetter().IsMatch(value) && LatinLetter().Matches(value).Count >= 3 && !LongNumber().IsMatch(value))
        {
            var converted = PersianText.Normalize(new string(original.Select(c => Layout.TryGetValue(c, out var fa) ? fa : Layout.TryGetValue(char.ToLowerInvariant(c), out fa) ? fa : c).ToArray()));
            var words = AssistantVocabulary.Tokens(converted).ToList();
            var known = words.Count(w => vocab.Contains(w) || vocab.IsSuffixed(w));
            if (words.Count > 0 && known >= Math.Max(1, (int)Math.Ceiling(words.Count * 0.6)) && words.Any(w => w.Length >= 3 && vocab.Contains(w)))
            {
                value = converted;
                fixes.Add("layout");
            }
        }

        // ۲. فینگلیش: دست‌کم دو واژهٔ واژه‌نامه (یا پیام تک‌واژه‌ای ≥۴ حرف)، تا یک «in» یا «be» داخل نام مدل عوض نشود.
        var latin = LatinWord().Matches(value).Select(m => m.Value).ToList();
        var dictionaryHits = latin.Count(Finglish.ContainsKey);
        var singleWord = latin.Count == 1 && WordCount(value) == 1 && latin[0].Length >= 4 && Finglish.ContainsKey(latin[0]);
        if (dictionaryHits >= 2 || singleWord)
        {
            value = LatinWord().Replace(value, m => Finglish.TryGetValue(m.Value, out var fa) ? fa : m.Value);
            fixes.Add("finglish");
        }

        // ۳. کشیدهٔ حروف: سه حرف تکراری یا بیشتر ← یک حرف.
        var stretched = Stretch().Replace(value, "$1");
        if (stretched != value)
        {
            value = stretched;
            fixes.Add("stretch");
        }

        // ۴. محاوره (واژهٔ کامل) و چسباندن دو بخشی‌ها.
        value = PersianWord().Replace(value, m =>
        {
            if (!Colloquial.TryGetValue(m.Value, out var formal))
                return m.Value;
            fixes.Add(m.Value + "→" + formal);
            return formal;
        });
        foreach (var (pattern, from, to, product) in JoinPatterns)
        {
            // نام کالا فقط وقتی چسبانده می‌شود که کاتالوگ همین شکل چسبیده را داشته باشد (یا کاتالوگی در دسترس نباشد).
            if (product && vocab.HasCatalog && !vocab.Contains(to))
                continue;
            var joined = pattern.Replace(value, to);
            if (joined != value)
            {
                value = joined;
                fixes.Add(from + "→" + to);
            }
        }

        // ۵. غلط تایپی (فقط واژه‌های فارسی ناآشنا).
        value = PersianWord().Replace(value, m =>
        {
            var fixedWord = Correct(m.Value, vocab);
            if (fixedWord is null)
                return m.Value;
            fixes.Add(m.Value + "→" + fixedWord);
            return fixedWord;
        });

        return fixes.Count == 0 ? new AssistantPrepared(original, original, []) : new AssistantPrepared(original, value, fixes);
    }

    /// <summary>
    /// اصلاح یک واژهٔ ناآشنا به نزدیک‌ترین واژهٔ آشنا، یا null. قانون‌ها (برای اینکه هرگز واژهٔ درست را خراب نکنیم):
    /// <list type="bullet">
    /// <item>واژهٔ ≤۳ حرفی، آشنا، یا آشنا + پسوند («باتریش») ← دست نمی‌خورد.</item>
    /// <item>نامزد باید با همان حرف شروع شود و فقط یک نامزد در کمترین فاصله باشد (اگر دو واژه به یک اندازه نزدیک‌اند، حدس نمی‌زنیم).</item>
    /// <item>۴ تا ۵ حرف: فقط یک جابه‌جایی حروف هم‌صدا (ت/ط، س/ص/ث، …)، جابه‌جایی دو حرف کنار هم، یا جا انداختن/اضافه کردن «ا و ی ه».</item>
    /// <item>۶ تا ۷ حرف: فاصلهٔ ۱؛ ۸ حرف و بیشتر: فاصلهٔ ۲.</item>
    /// </list>
    /// مثال: «باطری» ← «باتری»؛ «هدفن» ← «هدفون»؛ «سامسونگگ» ← «سامسونگ»؛ «زیگزاگ» ← null (نزدیکی ندارد).
    /// </summary>
    public static string? Correct(string word, AssistantVocabulary vocabulary)
    {
        if (word.Length < 4 || word.Length > 20 || vocabulary.Contains(word) || vocabulary.IsSuffixed(word) || !PersianLetter().IsMatch(word))
            return null;
        var max = word.Length >= 8 ? 2 : 1;
        string? best = null;
        var bestDistance = int.MaxValue;
        var ties = 0;
        foreach (var candidate in vocabulary.StartingWith(word[0]))
        {
            if (Math.Abs(candidate.Length - word.Length) > max || candidate.Length < 3)
                continue;
            var distance = Distance(word, candidate, max);
            if (distance > max)
                continue;
            if (word.Length <= 5 && !ShortEditAllowed(word, candidate))
                continue;
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
                ties = 1;
            }
            else if (distance == bestDistance && candidate != best)
            {
                ties++;
            }
        }
        return ties == 1 ? best : null;
    }

    /// <summary>
    /// فاصلهٔ ویرایشی (Damerau–Levenshtein محدود: افزودن، حذف، جایگزینی، جابه‌جایی دو حرف کنار هم). بیش از <paramref name="max"/> ← max+1 (توقف زودهنگام).
    /// مثال: («باطری»، «باتری») ← ۱؛ («هدفن»، «هدفون») ← ۱؛ («ابکر»، «اکبر») ← ۱.
    /// </summary>
    public static int Distance(string a, string b, int max = 2)
    {
        if (Math.Abs(a.Length - b.Length) > max)
            return max + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var value = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, d[i - 2, j - 2] + 1);
                d[i, j] = value;
                rowMin = Math.Min(rowMin, value);
            }
            if (rowMin > max)
                return max + 1;
        }
        return d[a.Length, b.Length];
    }

    /// <summary>برای واژهٔ کوتاه (۴–۵ حرف) فقط ویرایش‌های «طبیعی» پذیرفته می‌شود (توضیح در <see cref="Correct"/>).</summary>
    private static bool ShortEditAllowed(string word, string candidate)
    {
        if (word.Length == candidate.Length)
        {
            var diff = Enumerable.Range(0, word.Length).Where(i => word[i] != candidate[i]).ToList();
            if (diff.Count == 1)
                return Confusable.Any(g => g.Contains(word[diff[0]]) && g.Contains(candidate[diff[0]]));
            // جابه‌جایی دو حرف کنار هم: «ابکر» ↔ «اکبر».
            return diff.Count == 2 && diff[1] == diff[0] + 1 && word[diff[0]] == candidate[diff[1]] && word[diff[1]] == candidate[diff[0]];
        }
        var (longer, shorter) = word.Length > candidate.Length ? (word, candidate) : (candidate, word);
        for (var i = 0; i < longer.Length; i++)
            if (SoftLetters.Contains(longer[i]) && longer.Remove(i, 1) == shorter)
                return true;
        // حرف تکراری اضافه: «هدفوون» ← «هدفون».
        for (var i = 1; i < longer.Length; i++)
            if (longer[i] == longer[i - 1] && longer.Remove(i, 1) == shorter)
                return true;
        return false;
    }

    private static int WordCount(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>متن فارسی دارد؟ (برای تست و بخش‌های دیگر)</summary>
    public static bool HasPersian(string? text) => text is not null && PersianLetter().IsMatch(text);

    [GeneratedRegex(@"[\u0621-\u06CC-[\u064B-\u066D]]", RegexOptions.CultureInvariant)]
    private static partial Regex PersianLetter();

    [GeneratedRegex(@"[A-Za-z]", RegexOptions.CultureInvariant)]
    private static partial Regex LatinLetter();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])[a-z]+(?![\p{L}\p{N}])", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWord();

    [GeneratedRegex(@"[\u0621-\u06CC-[\u064B-\u066D]]+", RegexOptions.CultureInvariant)]
    private static partial Regex PersianWord();

    [GeneratedRegex(@"([\u0621-\u06CC])\1{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Stretch();

    [GeneratedRegex(@"\d{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumber();
}
