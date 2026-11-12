using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fratak_Shop.Core.Services.Branding;

// ═══════════════════════════════════════════════════════════════════════════
//  مرحلهٔ دوازدهم – «استودیوی لوگو»: مدل تنظیمات لوگو برای هر محل نمایش.
//
//  لوگوی فروشگاه = آیکون (تصویر) + متن برند در سه بخش: «بخش اول» (فراتک)، «بخش رنگی» (شاپ) و «شعار» اختیاری زیر آن.
//  برای هر محل (هدر، منوی موبایل، فوتر، ورود، پنل، فاکتور، ایمیل، صفحهٔ تعمیرات) جدا تعیین می‌شود:
//  نمایش آیکون/متن/شعار، آیکون اختصاصی، اندازهٔ آیکون (دسکتاپ و موبایل)، اندازه و وزن قلم، فاصلهٔ آیکون تا متن،
//  فاصلهٔ دو بخش متن، فاصلهٔ شعار، چینش (آیکون راست/چپ/بالا)، تراز، جابه‌جایی عمودی متن و رنگ‌ها.
//
//  ذخیره: ستون StoreSettings.BrandStyleJson (یک JSON). هر مقدار خالی (null) یعنی «پیش‌فرض همان محل»؛
//  پیش‌فرض‌ها دقیقاً همان ظاهر مرحلهٔ دهم را می‌سازند، پس تا مدیر چیزی عوض نکند هیچ چیز سایت تغییر نمی‌کند.
//  مثال JSON:
//    {"tagline":"خرید آنلاین لوازم جانبی",
//     "places":{"Header":{"showText":true,"iconHeight":48,"iconHeightMobile":36,"fontSize":24,"iconGap":10,"partGap":4},
//               "Footer":{"showTagline":true,"layout":"IconTop","align":"Center"}}}
//
//  محل استفاده:
//   • StoreSettingService.UpdateAsync → BrandStyleRules.Validate (هر مقدار بیرون از محدوده رد می‌شود).
//   • Web/Infrastructure/BrandIdentity.cs → BrandResolver.Resolve (ساخت مقدارهای نهایی هر محل).
//   • پنل ← تنظیمات ← استودیوی لوگو (Areas/Admin/Pages/Settings/Brand) برای ویرایش و پیش‌نمایش زنده.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// محل‌هایی که لوگو نمایش داده می‌شود. نام‌ها کلید JSON هستند؛ ترتیب/نام را عوض نکنید (فقط مقدار جدید به آخر اضافه کنید).
/// </summary>
public enum BrandPlace
{
    /// <summary>هدر سایت (دسکتاپ و موبایل؛ اندازهٔ موبایل جدا).</summary>
    Header,

    /// <summary>بالای منوی کشویی موبایل (دکمهٔ ☰).</summary>
    MobileMenu,

    /// <summary>فوتر سایت (زمینهٔ تیره).</summary>
    Footer,

    /// <summary>صفحه‌های ورود، ثبت‌نام و تأیید کد.</summary>
    Auth,

    /// <summary>سربرگ نوار کناری پنل مدیریت (زمینهٔ تیره).</summary>
    Admin,

    /// <summary>فاکتور مشتری و PDF آن (تصویر قابل‌چاپ PNG).</summary>
    Invoice,

    /// <summary>سربرگ ایمیل‌های فروشگاه (تصویر PNG، چون بیشتر برنامه‌های ایمیل SVG نشان نمی‌دهند).</summary>
    Email,

    /// <summary>صفحهٔ «در حال به‌روزرسانی» (حالت تعمیرات).</summary>
    Maintenance,
}

/// <summary>چینش آیکون نسبت به متن. در سایت راست‌به‌چپ «ابتدا» یعنی سمت راست.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BrandLayout>))]
public enum BrandLayout
{
    /// <summary>آیکون سمت راست (ابتدای خط) و متن کنارش – حالت معمول.</summary>
    IconStart,

    /// <summary>آیکون سمت چپ (انتهای خط) و متن قبل از آن.</summary>
    IconEnd,

    /// <summary>آیکون بالا و متن زیرش (مناسب فوتر، صفحهٔ ورود و ایمیل).</summary>
    IconTop,
}

/// <summary>
/// تراز. در چینش افقی = تراز عمودی آیکون و متن (بالا/وسط/پایین)؛ در چینش «آیکون بالا» = تراز افقی (راست/وسط/چپ).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BrandAlign>))]
public enum BrandAlign
{
    Center,
    Start,
    End,
}

/// <summary>
/// تنظیمات یک محل. هر مقدار null = پیش‌فرض آن محل (و اندازه‌ها «خودکار متناسب با آیکون»).
/// مثال: فقط IconHeight = 56 → قلم، فاصله‌ها و شعار خودکار متناسب با ۵۶ پیکسل بزرگ می‌شوند.
/// </summary>
public sealed class BrandPlaceStyle
{
    /// <summary>اصلاً لوگو نمایش داده شود؟ (فقط در محل‌هایی که CanHide دارند؛ مثلاً فوتر یا ایمیل).</summary>
    public bool? Visible { get; set; }

    public bool? ShowIcon { get; set; }

    public bool? ShowText { get; set; }

    /// <summary>نمایش شعار (بخش سوم) زیر یا کنار نام.</summary>
    public bool? ShowTagline { get; set; }

    /// <summary>آیکون اختصاصی این محل (مثلاً نسخهٔ افقیِ کامل برای فوتر). خالی = آیکون اصلی.</summary>
    public string? IconUrl { get; set; }

    /// <summary>آیکون اختصاصی این محل برای زمینهٔ تیره. خالی = آیکون تیرهٔ اصلی.</summary>
    public string? IconDarkUrl { get; set; }

    /// <summary>ارتفاع آیکون (پیکسل). مثال: ۴۴.</summary>
    public int? IconHeight { get; set; }

    /// <summary>ارتفاع آیکون در موبایل (عرض صفحه کمتر از ۷۶۸ پیکسل). خالی = مثل دسکتاپ.</summary>
    public int? IconHeightMobile { get; set; }

    /// <summary>اندازهٔ قلم نام (پیکسل). خالی = حدود ۰٫۶۵ ارتفاع آیکون.</summary>
    public int? FontSize { get; set; }

    /// <summary>اندازهٔ قلم نام در موبایل. خالی = به نسبت آیکون موبایل کوچک می‌شود.</summary>
    public int? FontSizeMobile { get; set; }

    /// <summary>وزن قلم نام: ۳۰۰ (نازک) تا ۹۰۰ (خیلی ضخیم). پیش‌فرض ۸۰۰.</summary>
    public int? FontWeight { get; set; }

    /// <summary>فاصلهٔ بین حروف (پیکسل، می‌تواند منفی باشد). مثال: -0.5.</summary>
    public double? LetterSpacing { get; set; }

    /// <summary>فاصلهٔ آیکون تا متن (پیکسل).</summary>
    public int? IconGap { get; set; }

    /// <summary>فاصلهٔ «بخش اول» تا «بخش رنگی» (پیکسل). ۰ = چسبیده، مثل «فراتک‌شاپ».</summary>
    public int? PartGap { get; set; }

    /// <summary>فاصلهٔ نام تا شعار (پیکسل).</summary>
    public int? TaglineGap { get; set; }

    /// <summary>اندازهٔ قلم شعار (پیکسل).</summary>
    public int? TaglineSize { get; set; }

    /// <summary>جابه‌جایی عمودی متن برای هم‌ترازی دقیق با آیکون (پیکسل؛ منفی = بالا). مثال: ۲.</summary>
    public int? TextOffsetY { get; set; }

    public BrandLayout? Layout { get; set; }

    public BrandAlign? Align { get; set; }

    /// <summary>متن اختصاصی بخش اول در این محل. خالی = متن اصلی برند.</summary>
    public string? Text1 { get; set; }

    /// <summary>متن اختصاصی بخش رنگی در این محل. «-» = بدون بخش دوم.</summary>
    public string? Text2 { get; set; }

    /// <summary>شعار اختصاصی این محل. خالی = شعار اصلی.</summary>
    public string? Tagline { get; set; }

    public string? Color1 { get; set; }

    public string? Color2 { get; set; }

    public string? TaglineColor { get; set; }

    /// <summary>هیچ مقداری تنظیم نشده (همه پیش‌فرض)؛ در JSON نوشته نمی‌شود.</summary>
    [JsonIgnore]
    public bool IsEmpty => GetType().GetProperties().Where(p => p.CanWrite).All(p => p.GetValue(this) is null);
}

/// <summary>
/// کل تنظیمات استودیوی لوگو: شعار مشترک، آیکون‌های تکمیلی اپ (PWA) و تنظیم هر محل.
/// </summary>
public sealed class BrandStyleConfig
{
    /// <summary>شعار مشترک (بخش سوم). خالی = «شعار» تنظیمات عمومی فروشگاه. مثال: «خرید آنلاین لوازم جانبی».</summary>
    public string? Tagline { get; set; }

    /// <summary>رنگ شعار روی زمینهٔ روشن. خالی = خاکستری #64748B (روی زمینهٔ تیره #94A3B8).</summary>
    public string? TaglineColor { get; set; }

    /// <summary>آیکون کوچک اپ (۱۹۲×۱۹۲). خالی = همان آیکون اصلی اپ.</summary>
    public string? PwaIcon192Url { get; set; }

    /// <summary>
    /// آیکون maskable اپ (۵۱۲×۵۱۲، زمینهٔ پر و نشان داخل ۸۰٪ وسط) تا اندروید آن را دایره/مربع گرد برش بزند.
    /// خالی = اگر آیکون اپ اختصاصی دارید maskable اعلام نمی‌شود؛ وگرنه آیکون maskable برند.
    /// </summary>
    public string? PwaMaskableIconUrl { get; set; }

    /// <summary>آیکون صفحهٔ اصلی آیفون (۱۸۰×۱۸۰، بدون شفافیت). خالی = آیکون اپ یا آیکون iOS برند.</summary>
    public string? AppleTouchIconUrl { get; set; }

    /// <summary>رنگ زمینهٔ صفحهٔ شروع اپ (background_color مانیفست). خالی = سفید برند.</summary>
    public string? PwaBackgroundColor { get; set; }

    /// <summary>آخرین رنگ زمینه‌ای که سازندهٔ خودکار آیکون اپ استفاده کرد (برای پر کردن دوبارهٔ فرم).</summary>
    public string? IconGenBackground { get; set; }

    /// <summary>آخرین حاشیهٔ داخلی سازندهٔ آیکون (درصد ۰ تا ۴۰).</summary>
    public int? IconGenPadding { get; set; }

    /// <summary>آخرین گردی گوشهٔ سازندهٔ آیکون (درصد ۰ تا ۵۰؛ ۵۰ = دایره).</summary>
    public int? IconGenRadius { get; set; }

    /// <summary>تنظیم هر محل؛ کلید = نام <see cref="BrandPlace"/>. مثال: places["Footer"].</summary>
    public Dictionary<string, BrandPlaceStyle> Places { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>تنظیم یک محل (اگر نباشد یک نمونهٔ خالی = همه پیش‌فرض).</summary>
    public BrandPlaceStyle For(BrandPlace place) =>
        Places.TryGetValue(place.ToString(), out var style) && style is not null ? style : new BrandPlaceStyle();
}

/// <summary>
/// مشخصات ثابت هر محل: عنوان فارسی، زمینهٔ تیره، داشتن اندازهٔ موبایل، امکان پنهان کردن و مقدارهای پیش‌فرض.
/// پیش‌فرض‌ها همان ظاهر قبلی‌اند: هدر ۴۴ پیکسل بدون متن، ورود ۴۴ با متن، پنل ۳۰ تیره، فاکتور ۴۰.
/// </summary>
/// <param name="Place">محل.</param>
/// <param name="Title">عنوان در پنل. مثال: «هدر سایت».</param>
/// <param name="Hint">توضیح کوتاه برای مدیر.</param>
/// <param name="OnDark">زمینهٔ همیشه تیره (آیکون تیره و متن سفید پیش‌فرض).</param>
/// <param name="Responsive">اندازهٔ جدا برای موبایل دارد.</param>
/// <param name="CanHide">می‌شود کل لوگو را در این محل خاموش کرد.</param>
/// <param name="Raster">به تصویر PNG نیاز دارد (PDF فاکتور، ایمیل).</param>
/// <param name="IconHeight">ارتفاع پیش‌فرض آیکون.</param>
/// <param name="ShowTextDefault">متن به‌طور پیش‌فرض نمایش داده شود (برای چهار محل قدیمی از تنظیمات BrandTextIn* خوانده می‌شود).</param>
/// <param name="ShowTaglineDefault">شعار پیش‌فرض نمایش داده شود.</param>
/// <param name="LayoutDefault">چینش پیش‌فرض.</param>
public sealed record BrandPlaceInfo(
    BrandPlace Place,
    string Title,
    string Hint,
    bool OnDark,
    bool Responsive,
    bool CanHide,
    bool Raster,
    int IconHeight,
    bool ShowTextDefault,
    bool ShowTaglineDefault,
    BrandLayout LayoutDefault);

/// <summary>
/// قواعد استودیوی لوگو: محدوده‌های مجاز، خواندن/نوشتن JSON بدون خطا و اعتبارسنجی.
/// همهٔ متدها خالص (بدون پایگاه داده) و تست‌پذیرند – تست‌ها: Tests/Unit/BrandStyleTests.cs.
/// </summary>
public static partial class BrandStyleRules
{
    // ── محدوده‌ها (پیکسل) ─────────────────────────────────────────────
    public const int MinIcon = 12, MaxIcon = 200;
    public const int MinFont = 8, MaxFont = 96;
    public const int MinTagline = 6, MaxTagline = 48;
    public const int MaxGap = 80;
    public const int MaxOffset = 30;
    public const double MaxLetterSpacing = 10;
    public const int MaxTextLength = 40;
    public const int MaxTaglineLength = 80;

    /// <summary>سقف اندازهٔ JSON ذخیره‌شده (برای جلوگیری از سوءاستفاده)؛ تنظیمات واقعی چند کیلوبایت است.</summary>
    public const int MaxJsonLength = 32_000;

    /// <summary>مشخصات همهٔ محل‌ها به ترتیب نمایش در پنل.</summary>
    public static IReadOnlyList<BrandPlaceInfo> Places { get; } =
    [
        new(BrandPlace.Header, "هدر سایت", "لوگوی بالای همهٔ صفحه‌ها؛ اندازهٔ موبایل جداست.", false, true, false, false, 44, false, false, BrandLayout.IconStart),
        new(BrandPlace.MobileMenu, "منوی موبایل", "بالای منوی کشویی که با دکمهٔ ☰ در موبایل باز می‌شود.", false, false, true, false, 44, false, false, BrandLayout.IconStart),
        new(BrandPlace.Footer, "فوتر سایت", "پایین همهٔ صفحه‌ها روی زمینهٔ تیره، کنار معرفی کوتاه فروشگاه.", true, true, true, false, 40, true, false, BrandLayout.IconStart),
        new(BrandPlace.Auth, "ورود و ثبت‌نام", "صفحه‌های ورود، ثبت‌نام، کد تأیید و ورود دومرحله‌ای.", false, true, false, false, 44, true, false, BrandLayout.IconStart),
        new(BrandPlace.Admin, "پنل مدیریت", "سربرگ نوار کناری پنل (زمینهٔ تیره).", true, false, false, false, 30, true, false, BrandLayout.IconStart),
        new(BrandPlace.Invoice, "فاکتور", "فاکتور مشتری و فایل PDF آن (تصویر PNG).", false, false, false, true, 40, true, false, BrandLayout.IconStart),
        new(BrandPlace.Email, "ایمیل‌ها", "سربرگ ایمیل‌های فروشگاه (تصویر PNG).", false, false, true, true, 40, true, false, BrandLayout.IconStart),
        new(BrandPlace.Maintenance, "صفحهٔ تعمیرات", "صفحهٔ «در حال به‌روزرسانی» وقتی سایت در حالت تعمیرات است.", false, false, true, false, 56, true, false, BrandLayout.IconTop),
    ];

    public static BrandPlaceInfo Info(BrandPlace place) => Places.First(p => p.Place == place);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 8,
    };

    /// <summary>
    /// خواندن JSON ذخیره‌شده؛ هرگز خطا نمی‌دهد. JSON خراب یا خالی = تنظیمات پیش‌فرض (سایت هرگز به خاطر لوگو نمی‌خوابد).
    /// مثال: Parse(null) → همه پیش‌فرض؛ Parse("{broken") → همه پیش‌فرض.
    /// </summary>
    public static BrandStyleConfig Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            return new BrandStyleConfig();
        try
        {
            var config = JsonSerializer.Deserialize<BrandStyleConfig>(json, Json) ?? new BrandStyleConfig();
            // کلیدها دوباره در دیکشنری بدون حساسیت به حروف ریخته می‌شوند و محل‌های ناشناخته کنار می‌روند.
            var places = new Dictionary<string, BrandPlaceStyle>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in config.Places ?? [])
                if (value is not null && Enum.TryParse<BrandPlace>(key, ignoreCase: true, out var place) && Enum.IsDefined(place))
                    places[place.ToString()] = value;
            config.Places = places;
            return config;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return new BrandStyleConfig();
        }
    }

    /// <summary>
    /// نوشتن JSON فشرده: مقدارهای خالی و محل‌های بدون تغییر نوشته نمی‌شوند. همه چیز پیش‌فرض = null (ستون خالی).
    /// </summary>
    public static string? Serialize(BrandStyleConfig config)
    {
        var places = config.Places
            .Where(p => p.Value is not null && !p.Value.IsEmpty)
            .OrderBy(p => Enum.TryParse<BrandPlace>(p.Key, true, out var place) ? (int)place : 99)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var copy = new BrandStyleConfig
        {
            Tagline = Blank(config.Tagline),
            TaglineColor = Blank(config.TaglineColor),
            PwaIcon192Url = Blank(config.PwaIcon192Url),
            PwaMaskableIconUrl = Blank(config.PwaMaskableIconUrl),
            AppleTouchIconUrl = Blank(config.AppleTouchIconUrl),
            PwaBackgroundColor = Blank(config.PwaBackgroundColor),
            IconGenBackground = Blank(config.IconGenBackground),
            IconGenPadding = config.IconGenPadding,
            IconGenRadius = config.IconGenRadius,
            Places = places,
        };
        var empty = copy.Places.Count == 0 && copy.Tagline is null && copy.TaglineColor is null && copy.PwaIcon192Url is null
                    && copy.PwaMaskableIconUrl is null && copy.AppleTouchIconUrl is null && copy.PwaBackgroundColor is null
                    && copy.IconGenBackground is null && copy.IconGenPadding is null && copy.IconGenRadius is null;
        if (empty)
            return null;
        if (copy.Places.Count == 0)
            copy.Places = null!;
        return JsonSerializer.Serialize(copy, Json);
    }

    /// <summary>
    /// بررسی همهٔ مقدارها؛ خروجی = پیام خطای فارسی یا null (معتبر). متن‌های خالی پیش از بررسی null می‌شوند.
    /// مثال خطا: ارتفاع آیکون هدر ۵۰۰ → «ارتفاع آیکون (هدر سایت) باید بین ۱۲ تا ۲۰۰ پیکسل باشد.»
    /// </summary>
    public static string? Validate(BrandStyleConfig config)
    {
        config.Tagline = Blank(config.Tagline);
        if (TextError(config.Tagline, MaxTaglineLength, "شعار") is { } taglineError)
            return taglineError;
        foreach (var (color, label) in new[] { (config.TaglineColor, "رنگ شعار"), (config.PwaBackgroundColor, "رنگ زمینهٔ اپ"), (config.IconGenBackground, "رنگ زمینهٔ آیکون") })
            if (!IsColorOrEmpty(color))
                return $"{label} باید کد هگز باشد (مثلاً #0069FD).";
        foreach (var (url, label) in new[] { (config.PwaIcon192Url, "آیکون کوچک اپ"), (config.PwaMaskableIconUrl, "آیکون maskable اپ"), (config.AppleTouchIconUrl, "آیکون آیفون") })
            if (!IsAssetUrlOrEmpty(url))
                return $"آدرس «{label}» باید با / شروع شود (فایل کتابخانهٔ رسانه) یا آدرس کامل http(s) باشد.";
        if (config.IconGenPadding is < 0 or > 40 || config.IconGenRadius is < 0 or > 50)
            return "حاشیهٔ آیکون (۰ تا ۴۰٪) یا گردی گوشه (۰ تا ۵۰٪) معتبر نیست.";

        foreach (var (key, style) in config.Places)
        {
            if (!Enum.TryParse<BrandPlace>(key, true, out var place) || !Enum.IsDefined(place))
                return $"محل لوگوی «{key}» شناخته نشده است.";
            if (style is null)
                continue;
            var title = Info(place).Title;
            if (PlaceError(style, title) is { } error)
                return error;
            // فاکتور (html2canvas) و ایمیل‌ها (جیمیل/اوت‌لوک) SVG را نشان نمی‌دهند؛ مثال: «/uploads/a.svg» برای فاکتور → خطا.
            if (Info(place).Raster && style.IconUrl is { } rasterIcon && BrandResolver.IsSvg(rasterIcon))
                return $"آیکون «{title}» باید PNG یا JPG باشد؛ این محل SVG را درست نشان نمی‌دهد.";
            if (style.Visible == false && !Info(place).CanHide)
                return $"لوگوی «{title}» را نمی‌شود کاملاً پنهان کرد.";
            if (style.ShowIcon == false && style.ShowText == false)
                return $"در «{title}» دست‌کم آیکون یا متن باید نمایش داده شود (برای پنهان کردن کامل از گزینهٔ نمایش لوگو استفاده کنید).";
        }
        return null;
    }

    private static string? PlaceError(BrandPlaceStyle s, string title)
    {
        s.IconUrl = Blank(s.IconUrl);
        s.IconDarkUrl = Blank(s.IconDarkUrl);
        s.Text1 = Blank(s.Text1);
        s.Text2 = Blank(s.Text2);
        s.Tagline = Blank(s.Tagline);
        s.Color1 = Blank(s.Color1);
        s.Color2 = Blank(s.Color2);
        s.TaglineColor = Blank(s.TaglineColor);

        if (!InRange(s.IconHeight, MinIcon, MaxIcon) || !InRange(s.IconHeightMobile, MinIcon, MaxIcon))
            return $"ارتفاع آیکون ({title}) باید بین {MinIcon} تا {MaxIcon} پیکسل باشد.";
        if (!InRange(s.FontSize, MinFont, MaxFont) || !InRange(s.FontSizeMobile, MinFont, MaxFont))
            return $"اندازهٔ قلم ({title}) باید بین {MinFont} تا {MaxFont} پیکسل باشد.";
        if (!InRange(s.TaglineSize, MinTagline, MaxTagline))
            return $"اندازهٔ قلم شعار ({title}) باید بین {MinTagline} تا {MaxTagline} پیکسل باشد.";
        if (s.FontWeight is { } weight && (weight < 100 || weight > 900 || weight % 100 != 0))
            return $"وزن قلم ({title}) باید یکی از ۱۰۰، ۲۰۰ … ۹۰۰ باشد.";
        if (!InRange(s.IconGap, 0, MaxGap) || !InRange(s.PartGap, 0, MaxGap) || !InRange(s.TaglineGap, 0, MaxGap))
            return $"فاصله‌ها ({title}) باید بین ۰ تا {MaxGap} پیکسل باشند.";
        if (!InRange(s.TextOffsetY, -MaxOffset, MaxOffset))
            return $"جابه‌جایی عمودی متن ({title}) باید بین -{MaxOffset} تا {MaxOffset} پیکسل باشد.";
        if (s.LetterSpacing is { } spacing && (double.IsNaN(spacing) || Math.Abs(spacing) > MaxLetterSpacing))
            return $"فاصلهٔ حروف ({title}) باید بین -{MaxLetterSpacing} تا {MaxLetterSpacing} پیکسل باشد.";
        if (s.LetterSpacing is { } ls)
            s.LetterSpacing = Math.Round(ls, 1);
        if (s.Layout is { } layout && !Enum.IsDefined(layout) || s.Align is { } align && !Enum.IsDefined(align))
            return $"چینش لوگو ({title}) معتبر نیست.";
        if (!IsAssetUrlOrEmpty(s.IconUrl) || !IsAssetUrlOrEmpty(s.IconDarkUrl))
            return $"آدرس آیکون اختصاصی ({title}) باید با / شروع شود یا آدرس کامل http(s) باشد.";
        if (!IsColorOrEmpty(s.Color1) || !IsColorOrEmpty(s.Color2) || !IsColorOrEmpty(s.TaglineColor))
            return $"رنگ‌های لوگو ({title}) باید کد هگز باشند (مثلاً #0069FD).";
        return TextError(s.Text1, MaxTextLength, $"متن بخش اول ({title})")
               ?? TextError(s.Text2, MaxTextLength, $"متن بخش رنگی ({title})")
               ?? TextError(s.Tagline, MaxTaglineLength, $"شعار ({title})");
    }

    private static string? TextError(string? text, int max, string label)
    {
        if (text is null)
            return null;
        if (text.Length > max)
            return $"{label} حداکثر {max} نویسه است.";
        return text.IndexOfAny(['<', '>', '{', '}']) >= 0 || text.Any(char.IsControl) ? $"{label} نباید < > {{ }} یا نویسهٔ کنترلی داشته باشد." : null;
    }

    private static bool InRange(int? value, int min, int max) => value is null || (value >= min && value <= max);

    /// <summary>فقط #RGB یا #RRGGBB (هر چیز دیگر، حتی تلاش برای تزریق CSS، نامعتبر است). خالی = معتبر.</summary>
    public static bool IsColorOrEmpty(string? value) => string.IsNullOrWhiteSpace(value) || HexRegex().IsMatch(value.Trim());

    /// <summary>رنگ hex معتبر یا null. مثال: « #0069fd » → «#0069fd»؛ «red» → null.</summary>
    public static string? Hex(string? value) => value is not null && HexRegex().IsMatch(value.Trim()) ? value.Trim() : null;

    /// <summary>
    /// آدرس فایل تصویر: مسیر سایت («/uploads/a.png»، نه «//host») یا آدرس کامل http(s)، بدون فاصله، گیومه و &lt; &gt;.
    /// خالی = معتبر. مثال نامعتبر: «javascript:alert(1)»، «/a b.png»، «/a.png" onerror="x».
    /// </summary>
    public static bool IsAssetUrlOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var url = value.Trim();
        if (url.Length > 500 || url.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '\\' or '`' or '(' or ')'))
            return false;
        if (url.StartsWith('/'))
            return !url.StartsWith("//");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>آدرس امن یا null.</summary>
    public static string? Asset(string? value) => !string.IsNullOrWhiteSpace(value) && IsAssetUrlOrEmpty(value) ? value.Trim() : null;

    /// <summary>متن خالی/فاصله = null؛ وگرنه بدون فاصلهٔ ابتدا و انتها.</summary>
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>عدد برای CSS با نقطهٔ انگلیسی. مثال: -0.5 → «-0.5».</summary>
    public static string Css(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexRegex();
}
