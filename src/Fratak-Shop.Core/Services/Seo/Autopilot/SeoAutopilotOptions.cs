using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fratak_Shop.Core.Services.Seo.Autopilot;

/// <summary>
/// حالت هر بخش خلبان خودکار سئو.
/// <list type="bullet">
/// <item><c>Off</c>: آن بخش کاری نمی‌کند.</item>
/// <item><c>Suggest</c>: فقط گزارش/پیشنهاد می‌سازد (در «کارهای سئو» دیده می‌شود) و چیزی را عوض نمی‌کند.</item>
/// <item><c>Auto</c>: خودش درست می‌کند و نسخهٔ قبلی را در «تاریخچهٔ سئو» نگه می‌دارد تا با یک کلیک برگردد.</item>
/// </list>
/// </summary>
public enum SeoAutoMode : byte
{
    Off = 0,
    Suggest = 1,
    Auto = 2,
}

/// <summary>
/// تنظیمات خلبان خودکار سئو (یک ردیف در جدول <c>SeoAutopilotConfigs</c>، ستون <c>OptionsJson</c>).
/// <para>چرا JSON؟ چون این تنظیمات زیاد و در حال رشدند؛ با JSON افزودن گزینهٔ تازه به مایگریشن نیاز ندارد و مقدار پیش‌فرض
/// گزینهٔ تازه برای سایت‌های قدیمی هم خودکار اعمال می‌شود.</para>
/// <para>کجا استفاده می‌شود؟ صفحهٔ «پنل ← سئو ← خلبان خودکار»، سرویس <c>SeoAutopilotService</c> و کارگر پس‌زمینه.</para>
/// <para>پیش‌فرض‌ها طوری انتخاب شده‌اند که سایت بدون کارشناس سئو و بدون ریسک جریمه کار کند:
/// پر کردن فیلدهای خالی، مرتب‌سازی HTML، تصویر و ریدایرکت «خودکار»؛ بازنویسی متن‌های موجود فقط با دادهٔ واقعی
/// (CTR سرچ کنسول) و با فاصلهٔ حداقل ۲۱ روز و سقف هفتگی.</para>
/// </summary>
public sealed class SeoAutopilotOptions
{
    /// <summary>کلید اصلی: خاموش ← هیچ بخشی از خلبان خودکار کار نمی‌کند (قانون‌های ذخیره مثل نامک انگلیسی همیشه فعال‌اند).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>پر کردن خودکار فیلدهای خالی سئو (عنوان، توضیحات، کلمهٔ کلیدی، اوپن‌گراف، اسکیما، نقشهٔ سایت، Alt تصویر).</summary>
    public SeoAutoMode FillFields { get; set; } = SeoAutoMode.Auto;

    /// <summary>مرتب‌سازی HTML محتوایی که ادمین وارد می‌کند (کامنت، H1 تکراری، Alt، لینک خالی، استایل Word و ...).</summary>
    public SeoAutoMode FixHtml { get; set; } = SeoAutoMode.Auto;

    /// <summary>تبدیل تصویرها به WebP و Alt خودکار.</summary>
    public SeoAutoMode Images { get; set; } = SeoAutoMode.Auto;

    /// <summary>ریدایرکت خودکار آدرس‌های ۴۰۴ پربازدید به نزدیک‌ترین صفحهٔ واقعی.</summary>
    public SeoAutoMode Redirects { get; set; } = SeoAutoMode.Auto;

    /// <summary>بازنویسی عنوان/توضیحات صفحه‌هایی که CTR پایین دارند (فقط با دادهٔ واقعی، با آزمون و برگشت خودکار).</summary>
    public SeoAutoMode Rewrite { get; set; } = SeoAutoMode.Auto;

    /// <summary>لینک‌سازی داخلی خودکار در متن مقاله‌ها/محصولات (هنگام نمایش، بدون دست زدن به متن ذخیره‌شده).</summary>
    public SeoAutoMode InternalLinks { get; set; } = SeoAutoMode.Auto;

    /// <summary>صفحه‌های فرود «بهترین ... زیر ... تومان» و «مقایسهٔ ... و ...» که از دادهٔ واقعی محصولات ساخته می‌شوند.</summary>
    public SeoAutoMode Landings { get; set; } = SeoAutoMode.Auto;

    /// <summary>
    /// دور ۳۰ (#۹) – «بالا بردن نمرهٔ صفحه‌های ضعیف»: هر شب صفحه‌هایی که نمرهٔ سئویشان زیر <see cref="RaiseTarget"/> است
    /// (کالا، دسته، مقاله، برند، صفحه) پیدا و بخش‌های ضعیفشان با موتور سئو بهتر می‌شود؛ هر تغییر در تاریخچه با امکان برگشت.
    /// </summary>
    public SeoAutoMode RaiseLowScores { get; set; } = SeoAutoMode.Auto;

    /// <summary>دور ۳۰ (#۹): نمرهٔ هدف (۶۰ تا ۹۵). صفحهٔ زیر این نمره در صف بهبود است.</summary>
    public int RaiseTarget { get; set; } = 85;

    /// <summary>دور ۳۰ (#۹): بیشترین صفحهٔ بهبودیافته در هر شب (۵ تا ۳۰۰) تا بار سرور و تغییر ناگهانی کنترل شود.</summary>
    public int RaiseNightly { get; set; } = 60;

    /// <summary>دور ۳۰ (#۹): فاصلهٔ دو بهبود یک صفحه (۷ تا ۱۸۰ روز) – تغییر پشت‌سرهم یک صفحه برای گوگل نشانهٔ بدی است.</summary>
    public int RaiseCooldownDays { get; set; } = 14;

    /// <summary>اعلام فوری صفحه‌های تازه/تغییرکرده به بینگ و یاندکس (IndexNow – رایگان).</summary>
    public bool IndexNow { get; set; } = true;

    /// <summary>خزش شبانهٔ خود سایت (مثل Screaming Frog) و ساخت «کارهای سئو».</summary>
    public bool Crawl { get; set; } = true;

    /// <summary>ثبت بازدید ربات‌های موتور جست‌وجو (گوگل‌بات، بینگ‌بات ...) با تشخیص ربات جعلی.</summary>
    public bool BotLog { get; set; } = true;

    /// <summary>جمع‌آوری سرعت واقعی کاربران (Core Web Vitals) با اسکریپت کوچک و بدون کوکی.</summary>
    public bool RealUserMetrics { get; set; } = true;

    /// <summary>گزارش هفتگی سئو برای مدیران (اعلان پنل).</summary>
    public bool WeeklyReport { get; set; } = true;

    /// <summary>API محصولات ترب (نسخهٔ ۳) و فید عمومی محصولات.</summary>
    public bool ProductFeeds { get; set; } = true;

    /// <summary>ساعت اجرای کارهای شبانه به وقت تهران (۰ تا ۲۳). پیش‌فرض ۴ صبح که بازدید کمتر است.</summary>
    public int NightlyHour { get; set; } = 4;

    /// <summary>کمترین فاصله بین دو بازنویسی یک صفحه (روز). کمتر از ۲۱ مجاز نیست (گوگل باید فرصت سنجش داشته باشد).</summary>
    public int RewriteCooldownDays { get; set; } = 21;

    /// <summary>سقف درصد صفحه‌هایی که در یک هفته بازنویسی می‌شوند (تغییر ناگهانی و انبوه سیگنال بدی است).</summary>
    public int RewriteWeeklyPercent { get; set; } = 5;

    /// <summary>بیشترین لینک داخلی خودکار در هر صفحه.</summary>
    public int MaxAutoLinks { get; set; } = 6;

    /// <summary>بیشترین صفحه در هر خزش شبانه.</summary>
    public int CrawlMaxPages { get; set; } = 1500;

    /// <summary>بیشترین صفحهٔ فرود «بهترین زیر قیمت» و «مقایسه» (جلوگیری از صفحه‌سازی انبوه = Scaled Content).</summary>
    public int MaxLandingPages { get; set; } = 60;

    /// <summary>واژه‌هایی که هرگز نباید در متن‌های خودکار بیایند (با ویرگول جدا شوند). مثال: «ارزان‌ترین، اورجینال».</summary>
    public string? BlockedWords { get; set; }

    /// <summary>نام نویسندهٔ پیش‌فرض مقاله‌ها وقتی نویسنده ثبت نشده (خالی ← «تیم محتوای» + نام سایت).</summary>
    public string? DefaultAuthor { get; set; }

    /// <summary>واژه‌های ممنوع به صورت فهرست تمیز.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> BlockedWordList =>
        (BlockedWords ?? "").Split([',', '،', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 1).Distinct(StringComparer.Ordinal).ToList();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// خواندن از JSON. JSON خالی/خراب ← پیش‌فرض‌ها (سایت هرگز به خاطر تنظیمات خراب از کار نمی‌افتد).
    /// مقدارهای خارج از بازه به بازهٔ امن برگردانده می‌شوند. مثال: <c>Parse("{\"RewriteCooldownDays\":3}").RewriteCooldownDays == 21</c>.
    /// </summary>
    public static SeoAutopilotOptions Parse(string? json)
    {
        SeoAutopilotOptions? options = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                options = JsonSerializer.Deserialize<SeoAutopilotOptions>(json, Json);
            }
            catch (JsonException)
            {
                options = null;
            }
        }

        return (options ?? new SeoAutopilotOptions()).Normalized();
    }

    /// <summary>JSON فشرده برای ذخیره.</summary>
    public string ToJson() => JsonSerializer.Serialize(Normalized(), Json);

    /// <summary>برگرداندن عددها به بازهٔ امن (حتی اگر کسی JSON را دستی عوض کند).</summary>
    public SeoAutopilotOptions Normalized()
    {
        NightlyHour = Math.Clamp(NightlyHour, 0, 23);
        RewriteCooldownDays = Math.Clamp(RewriteCooldownDays, 21, 365);
        RewriteWeeklyPercent = Math.Clamp(RewriteWeeklyPercent, 1, 20);
        MaxAutoLinks = Math.Clamp(MaxAutoLinks, 0, 15);
        CrawlMaxPages = Math.Clamp(CrawlMaxPages, 50, 10000);
        MaxLandingPages = Math.Clamp(MaxLandingPages, 0, 300);
        RaiseTarget = Math.Clamp(RaiseTarget, 60, 95);
        RaiseNightly = Math.Clamp(RaiseNightly, 5, 300);
        RaiseCooldownDays = Math.Clamp(RaiseCooldownDays, 7, 180);
        BlockedWords = string.IsNullOrWhiteSpace(BlockedWords) ? null : BlockedWords.Trim();
        if (BlockedWords is { Length: > 2000 })
            BlockedWords = BlockedWords[..2000];
        DefaultAuthor = string.IsNullOrWhiteSpace(DefaultAuthor) ? null : DefaultAuthor.Trim();
        if (DefaultAuthor is { Length: > 100 })
            DefaultAuthor = DefaultAuthor[..100];
        if (!Enum.IsDefined(FillFields)) FillFields = SeoAutoMode.Auto;
        if (!Enum.IsDefined(FixHtml)) FixHtml = SeoAutoMode.Auto;
        if (!Enum.IsDefined(Images)) Images = SeoAutoMode.Auto;
        if (!Enum.IsDefined(Redirects)) Redirects = SeoAutoMode.Auto;
        if (!Enum.IsDefined(Rewrite)) Rewrite = SeoAutoMode.Auto;
        if (!Enum.IsDefined(InternalLinks)) InternalLinks = SeoAutoMode.Auto;
        if (!Enum.IsDefined(Landings)) Landings = SeoAutoMode.Auto;
        if (!Enum.IsDefined(RaiseLowScores)) RaiseLowScores = SeoAutoMode.Auto;
        return this;
    }

    /// <summary>آیا این بخش اجازهٔ تغییر خودکار دارد؟ (کلید اصلی هم باید روشن باشد).</summary>
    public bool Can(SeoAutoMode mode) => Enabled && mode == SeoAutoMode.Auto;
}
