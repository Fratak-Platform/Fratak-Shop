using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Smart;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Smart;

/// <summary>
/// یک قانون «یادآوری تمدید کالای مصرفی» (C20): کالاهای یک دسته معمولاً هر چند روز تمام می‌شوند.
/// <para>مثال: دستهٔ «فیلتر تصفیه آب» با ۱۸۰ روز ← ۱۸۰ روز بعد از تحویل، یک پیام «وقت تعویض فیلتر است» می‌رود.</para>
/// </summary>
public sealed class ReplenishRule
{
    public Guid CategoryId { get; set; }

    /// <summary>چند روز بعد از تحویل یادآوری شود (۷ تا ۷۳۰).</summary>
    public int Days { get; set; } = 30;
}

/// <summary>
/// تنظیمات امکانات هوشمند دور نوزدهم (ستون OptionsJson جدول SmartContentConfigs).
/// <para>چرا JSON؟ افزودن گزینهٔ تازه مهاجرت نمی‌خواهد و پیش‌فرض گزینهٔ تازه برای سایت‌های قدیمی خودکار اعمال می‌شود.</para>
/// <para>پیش‌فرض‌ها «بی‌خطر» انتخاب شده‌اند: هر چیزی که در سایت دیده می‌شود فقط با اطمینان بالا خودکار اعمال می‌شود
/// و بقیه منتظر تأیید کارشناس می‌ماند. صفحهٔ تنظیم: پنل ← محتوای هوشمند ← تنظیمات.</para>
/// </summary>
public sealed class SmartOptions
{
    /// <summary>کلید اصلی: خاموش ← کارگر هوشمند هیچ کاری از صف برنمی‌دارد (دکمه‌ها کار را فقط در صف می‌گذارند).</summary>
    public bool Enabled { get; set; } = true;

    // ── عکس‌یاب (B1–B4) ────────────────────────────────────────────

    /// <summary>کمترین ضلع بلند عکس قابل قبول (پیکسل). ترب عکس زیر ۶۰۰ را نشان نمی‌دهد؛ پیش‌فرض ۶۰۰.</summary>
    public int MinImageSide { get; set; } = 600;

    /// <summary>حداکثر عکسی که در هر جست‌وجو بررسی می‌شود (هر عکس دانلود و تحلیل می‌شود؛ روی هاست ضعیف کم نگه دارید).</summary>
    public int MaxImageCandidates { get; set; } = 40;

    /// <summary>«تکمیل هوشمند» اگر محصول کمتر از این تعداد عکس داشت، بهترین عکس‌های با اطمینان بالا را خودکار اضافه می‌کند.</summary>
    public int AutoImagesWhenFewerThan { get; set; } = 5;

    /// <summary>حداکثر عکسی که خودکار اضافه می‌شود (بقیه برای انتخاب کارشناس می‌ماند).</summary>
    public int AutoImagesMax { get; set; } = 5;

    /// <summary>
    /// دور بیستم: هیچ عکسی بدون تأیید کارشناس روی محصول نمی‌نشیند (پیش‌فرض روشن). عکس اشتباه یعنی مشتری یک چیز می‌بیند و
    /// چیز دیگری می‌خرد؛ پس عکس‌یاب ۱۰ تا ۱۵ نامزد می‌آورد و کارشناس درست‌ها را انتخاب می‌کند (یا «رد همه و جست‌وجوی دوباره»).
    /// خاموش ← رفتار دور نوزدهم: عکس‌های منابع «همان کالا» تا سقف <see cref="AutoImagesMax"/> خودکار اضافه می‌شوند.
    /// </summary>
    public bool ImagesNeedApproval { get; set; } = true;

    /// <summary>دور بیستم: چند عکس نامزد برای انتخاب کارشناس آماده شود (۶ تا ۱۵؛ پیش‌فرض ۱۲).</summary>
    public int ImageCandidates { get; set; } = 12;

    // ── مشخصات و نظرها (B7–B13) ───────────────────────────────────

    /// <summary>ردیف مشخصاتی که دست‌کم این تعداد منبع روی آن توافق دارند خودکار اضافه می‌شود (فقط ردیف‌های تازه، نه تغییر ردیف موجود).</summary>
    public int AutoSpecMinSources { get; set; } = 2;

    /// <summary>جمع‌بندی نظرها وقتی خودکار «تأیید» و در سایت نمایش داده می‌شود که دست‌کم این تعداد نظر واقعی تحلیل شده باشد.</summary>
    public int AutoApproveInsightMinReviews { get; set; } = 8;

    /// <summary>
    /// دور بیستم: «تکمیل هوشمند» برای محصول «توضیحات محصول» حرفه‌ای (HTML با تیتر، خلاصه، جدول، نقاط قوت و جمع‌بندی) هم بنویسد.
    /// متن کم‌محتوا یا نوشتهٔ قبلی خود دستیار فقط وقتی همهٔ محافظ‌های جریمه قبول شوند خودکار جایگزین می‌شود؛ متن بلند انسانی
    /// هرگز بی‌اجازه عوض نمی‌شود (پیش‌نمایش برای تأیید کارشناس).
    /// </summary>
    public bool AutoReview { get; set; } = true;

    /// <summary>
    /// دور ۲۳: «تکمیل پیوسته» – در ۲۴ ساعت حداکثر چند کالای ناقص خودکار «تکمیل هوشمند» شوند (۰ = خاموش؛ پیش‌فرض ۱۲۰، حداکثر ۵۰۰).
    /// کارگر هر ۱۵ دقیقه سهمی از این سقف را در صف می‌گذارد (نه فقط شب‌ها). کالایی که در ۷ روز گذشته تکمیل شده دوباره انتخاب نمی‌شود.
    /// (نام قدیمی «شبانه» برای سازگاری با تنظیمات ذخیره‌شده مانده است.)
    /// </summary>
    public int NightlyCompletePerDay { get; set; } = 120;

    /// <summary>
    /// نسخهٔ تنظیمات ذخیره‌شده (برای ارتقای خودکار پیش‌فرض‌های قدیمی). مثال: تنظیمات دور ۲۰ با «۲۰ کالا در روز» (پیش‌فرض قدیم)
    /// ← بار اول بعد از دور ۲۳ به ۱۲۰ می‌رسد؛ عددی که مدیر خودش تغییر داده بود دست نمی‌خورد.
    /// </summary>
    public int OptionsVersion { get; set; }

    /// <summary>نسخهٔ فعلی تنظیمات.</summary>
    public const int CurrentVersion = 29;

    /// <summary>دور بیستم: برای هر نتیجهٔ کار هوشمند اعلان پنل (با لینک مستقیم به همان بخش) فرستاده شود.</summary>
    public bool NotifyResults { get; set; } = true;

    // ── بلاگ هوشمند (B14–B18) ─────────────────────────────────────

    /// <summary>برنامه‌ریز تقویم محتوا روشن باشد؟ (هر هفته ایده‌های تازه از دسته‌ها و جست‌وجوهای بی‌نتیجه)</summary>
    public bool BlogPlanner { get; set; } = true;

    /// <summary>چند مقاله در هفته (۰ تا ۷).</summary>
    public int BlogPostsPerWeek { get; set; } = 2;

    /// <summary>مقاله‌ای که از دروازهٔ کیفیت بگذرد خودکار منتشر شود؟ (خاموش ← همیشه پیش‌نویس)</summary>
    public bool BlogAutoPublish { get; set; } = true;

    /// <summary>کمترین امتیاز دروازهٔ کیفیت برای انتشار خودکار (۰ تا ۱۰۰).</summary>
    public int BlogQualityMin { get; set; } = 75;

    // ── یادآوری‌های مشتری (C19–C21) ───────────────────────────────

    /// <summary>درخواست نظر بعد از تحویل (C19).</summary>
    public bool ReviewRequests { get; set; } = true;

    /// <summary>چند روز بعد از تحویل درخواست نظر برود (۱ تا ۶۰).</summary>
    public int ReviewRequestAfterDays { get; set; } = 7;

    /// <summary>درخواست نظر با پیامک هم برود؟ (پیامک هزینه دارد؛ اعلان سایت و پوش همیشه رایگان است.)</summary>
    public bool ReviewRequestSms { get; set; } = true;

    public string ReviewRequestTitle { get; set; } = "نظرتان دربارهٔ «{ProductName}» چیست؟";

    public string ReviewRequestBody { get; set; } = "چند روز از رسیدن سفارشتان گذشته. تجربهٔ واقعی شما به خریداران بعدی کمک می‌کند درست انتخاب کنند.";

    public string ReviewRequestSmsText { get; set; } = "{SiteName}: نظر شما درباره «{ProductName}» به خریداران دیگر کمک می‌کند. ثبت نظر: {Link}\nلغو11";

    /// <summary>یادآوری تمدید کالای مصرفی (C20) – فقط برای دسته‌هایی که قانون دارند.</summary>
    public bool ReplenishReminders { get; set; } = true;

    public List<ReplenishRule> ReplenishRules { get; set; } = [];

    public string ReplenishTitle { get; set; } = "وقت تهیهٔ دوبارهٔ «{ProductName}» است؟";

    public string ReplenishBody { get; set; } = "معمولاً این کالا حدود {Days} روز کار می‌کند. اگر لازم دارید، از همین‌جا دوباره سفارش دهید.";

    public string ReplenishSmsText { get; set; } = "{SiteName}: «{ProductName}» احتمالاً رو به اتمام است. سفارش دوباره: {Link}\nلغو11";

    /// <summary>ساعت شروع «سکوت شبانه» به وقت ایران (C21): هیچ پیام تبلیغاتی/یادآوری در این بازه فرستاده نمی‌شود.</summary>
    public int QuietFromHour { get; set; } = 22;

    /// <summary>ساعت پایان سکوت شبانه.</summary>
    public int QuietToHour { get; set; } = 9;

    /// <summary>دور ۲۵ (الف): وقتی کالای «علاقه‌مندی‌ها»ی مشتری ارزان شد خبرش کنیم (اعلان سایت + پوش؛ بدون پیامک).</summary>
    public bool FavoritePriceAlert { get; set; } = true;

    /// <summary>دور ۲۵ (الف): کمترین درصد کاهش قیمت برای اعلان علاقه‌مندی‌ها (۳ تا ۵۰؛ پیش‌فرض ۵).</summary>
    public int FavoritePriceDropPercent { get; set; } = 5;

    // ── مدیریت (C24–C27) ──────────────────────────────────────────

    /// <summary>کالای موجودی که این تعداد روز فروش نداشته «کم‌گردش» است (C25).</summary>
    public int DeadStockWarnDays { get; set; } = 60;

    /// <summary>کالای موجودی که این تعداد روز فروش نداشته «راکد» است (C25).</summary>
    public int DeadStockCriticalDays { get; set; } = 90;

    /// <summary>
    /// محافظ سود (C27): ذخیرهٔ قیمت/تخفیف کمتر از قیمت خرید مسدود شود. خاموش کردنش فقط برای حراج‌های عمدی است.
    /// </summary>
    public bool BlockBelowCost { get; set; } = true;

    // ── رشد خودکار و استودیوی طراحی (دور ۲۱) ───────────────────────

    /// <summary>
    /// «رشد خودکار» روزانه: بالا بردن نمرهٔ سئوی ضعیف‌ترین صفحه‌های پربازدید، تکمیل کالاهای پربازدیدِ ناقص و اگر بازدید دیروز خیلی کمتر
    /// از معمول بود، جلو انداختن مقالهٔ بعدی و ایدهٔ بنر بیشتر. همه در پنل ← محتوای هوشمند ← رشد خودکار گزارش می‌شود. پیش‌فرض روشن.
    /// </summary>
    public bool GrowthAutopilot { get; set; } = true;

    /// <summary>حداکثر ایدهٔ بنر تازه در روز برای تأیید مدیر (۰ = خاموش، حداکثر ۶). ایده‌ها تا تأیید هیچ‌جای سایت دیده نمی‌شوند.</summary>
    public int BannerIdeasPerDay { get; set; } = 3;

    /// <summary>
    /// دور ۲۳: در روزهای سوگواری رسمی (تاسوعا، عاشورا، اربعین، ۲۸ صفر، ۲۱ رمضان، ۱۴ خرداد و …) بنرهای تبلیغاتی صفحهٔ نخست موقتاً
    /// نمایش داده نشوند (فقط بنرهایی که گروه نسخهٔ آن‌ها «mourning» است دیده می‌شوند). پیش‌فرض خاموش (خواستهٔ مدیر).
    /// محاسبه: <see cref="Fratak_Shop.Core.Services.Marketing.MourningCalendar"/>.
    /// </summary>
    public bool PauseBannersOnMourning { get; set; }

    /// <summary>
    /// دور ۲۳: آزمون A/B بنرها – بنرهای هم‌جایگاه با «گروه نسخه» یکسان به نوبت نشان داده می‌شوند؛ بعد از دست‌کم ۳۰۰ نمایش برای هر نسخه،
    /// نسخه‌ای که نرخ کلیک بهتری دارد بیشتر نشان داده می‌شود (نه همیشه؛ تا نسخهٔ دیگر هم فرصت داشته باشد). پیش‌فرض روشن.
    /// </summary>
    public bool BannerAbTest { get; set; } = true;

    // ── دستیار فروشگاه (دور ۲۴) ───────────────────────────────────

    /// <summary>
    /// دور ۲۴: نام دستیار هوشمند که در ویجت و معرفی دیده می‌شود (حداکثر ۲۰ نویسه؛ پیش‌فرض «آوا»).
    /// مثال: «آوا» ← «سلام! من آوا هستم، دستیار هوشمند فراتک.»
    /// </summary>
    public string AssistantName { get; set; } = "آوا";

    /// <summary>دور ۲۴: حباب خوش‌آمد کوتاه کنار دکمهٔ دستیار (فقط یک بار در هر نشست مرورگر). پیش‌فرض روشن.</summary>
    public bool AssistantGreeting { get; set; } = true;

    /// <summary>دور ۲۴: «گفت‌وگو با کارشناس» از داخل چت (اعلان به کارشناسان پشتیبانی). خاموش ← فقط شماره و تیکت نشان داده می‌شود.</summary>
    public bool AssistantHandoff { get; set; } = true;

    // ── تجربهٔ خرید (دور ۲۴، بستهٔ B) ─────────────────────────────

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۱۱): فرم «قیمت کمتر دیدید؟» در صفحهٔ کالا برای مشتری واردشده. هر درخواست را مدیر بررسی می‌کند و
    /// در صورت تأیید یک کد تخفیف شخصی و کوتاه‌مدت فقط برای همان کالا ساخته می‌شود. پیش‌فرض روشن.
    /// </summary>
    public bool PriceMatchEnabled { get; set; } = true;

    /// <summary>
    /// دور ۲۴ (پیشنهاد ۱۵): نشان دادن آمار واقعی خرید اخیر در صفحهٔ کالا (مثلاً «۱۲ نفر در ۷ روز گذشته خریده‌اند»).
    /// فقط عدد واقعی و فقط وقتی به حد معنادار برسد (هیچ عدد ساختگی یا شمارش معکوس دروغین). پیش‌فرض روشن.
    /// </summary>
    public bool SocialProof { get; set; } = true;

    /// <summary>دور ۲۴ (پیشنهاد ۲۹): جست‌وجو با عکس (دکمهٔ دوربین کنار جست‌وجو). پیش‌فرض روشن.</summary>
    public bool ImageSearch { get; set; } = true;

    // ── برگرداندن مشتری (دور ۲۴، پیشنهاد ۵۷) ─────────────────────────

    /// <summary>
    /// پیام برگشت خودکار برای مشتری‌هایی که مدل ریزش (ریتم خرید خودشان) می‌گوید در حال رفتن‌اند: کد تخفیف شخصی یک‌بارمصرف
    /// با سقف مبلغ + ۳ پیشنهاد هدیهٔ مرتبط. فقط اعلان سایت و پوش (بدون پیامک). پیش‌فرض خاموش چون هزینه دارد.
    /// </summary>
    public bool WinBackEnabled { get; set; }

    // ── دور ۲۹: دستیار (جای دکمه، هویت، صدا) ─────────────────────────

    /// <summary>
    /// دور ۲۹: جای پیش‌فرض دکمهٔ دستیار روی صفحه. مقدارها: «br» پایین راست، «bl» پایین چپ، «mr» وسط راست، «ml» وسط چپ.
    /// پیش‌فرض «br» (پایین راست) چون سمت چپ با دکمه‌ها و پنجره‌های دیگر (سبد خرید، اعلان‌ها) تداخل داشت.
    /// </summary>
    public string AssistantPosition { get; set; } = "br";

    /// <summary>دور ۲۹: مشتری بتواند دکمهٔ دستیار را با کشیدن جابه‌جا کند (جای انتخابی در مرورگر خودش می‌ماند). پیش‌فرض روشن.</summary>
    public bool AssistantDraggable { get; set; } = true;

    /// <summary>دور ۲۹: زیرعنوان سربرگ پنجرهٔ دستیار (حداکثر ۴۰ نویسه). مثال: «دستیار هوشمند فروشگاه · پاسخ خودکار».</summary>
    public string AssistantSubtitle { get; set; } = "دستیار هوشمند فروشگاه · پاسخ خودکار";

    /// <summary>
    /// دور ۲۹: لوگوی دستیار (آدرس داخلی یک تصویر آپلودشده، مثلاً «/uploads/media/ava.webp»). خالی ← آیکون ربات.
    /// فقط مسیر داخلی پذیرفته می‌شود (آدرس بیرونی یا «//» ← خالی) تا هیچ تصویری از سایت دیگر بار نشود.
    /// </summary>
    public string? AssistantLogo { get; set; }

    /// <summary>دور ۲۹: رنگ اصلی دکمه و سربرگ دستیار (هگز ۶ رقمی؛ نامعتبر ← فیروزه‌ای پیش‌فرض).</summary>
    public string AssistantColor { get; set; } = "#0f766e";

    /// <summary>دور ۲۹: «پیام صوتی» – مشتری با میکروفون صحبت کند و متنش (با تشخیص گفتار خود مرورگر، رایگان) فرستاده شود. پیش‌فرض روشن.</summary>
    public bool AssistantVoiceInput { get; set; } = true;

    /// <summary>دور ۲۹: «پاسخ صوتی» – پاسخ متنی دستیار با صدای خود مرورگر هم خوانده شود (مشتری می‌تواند خاموشش کند). پیش‌فرض خاموش.</summary>
    public bool AssistantVoiceReply { get; set; }

    // ── دور ۲۹: عکس کالا ────────────────────────────────────────────

    /// <summary>
    /// دور ۲۹: کمترین تعداد عکس هر کالا (پیش‌فرض ۵). کار روزانه برای کالاهای کمتر از این عدد عکس تازه پیدا می‌کند؛
    /// فقط عکس «دقیقاً همان کالا» (منبع مطمئن یا همان مدل + شباهت ظاهری با عکس اصلی) خودکار اضافه می‌شود و بقیه منتظر تأیید می‌ماند.
    /// </summary>
    public int MinProductImages { get; set; } = 5;

    /// <summary>دور ۲۹: عکس اصلی هر کالا خودکار «دوربری‌شده» (زمینهٔ شفاف، قاب یکسان) شود. پیش‌فرض روشن.</summary>
    public bool PrimaryCutout { get; set; } = true;

    /// <summary>دور ۲۹: لوگو/واترمارک عکس‌ها (گوشه‌ها، نوشتهٔ کم‌رنگ روی زمینه) خودکار پاک شود؛ پاک‌نشدنی ← عکس کنار می‌رود. پیش‌فرض روشن.</summary>
    public bool CleanWatermarks { get; set; } = true;

    /// <summary>
    /// دور ۲۹ (#۱۰): بازبینی روزانهٔ دستهٔ کالاها. کالایی که آشکارا در دستهٔ اشتباه است (مثل «ظرف شیش‌پارچه» در «چندراهی برق»)
    /// خودکار به دستهٔ درست می‌رود (حداکثر ۲۵ کالا در روز، با اعلان)؛ کالای مشکوک فقط گزارش می‌شود.
    /// </summary>
    public bool Recategorize { get; set; } = true;

    /// <summary>دور ۲۹: لوگوی کم‌رنگ فراتک روی عکس کالاها (فقط نسخه‌های بهینهٔ نمایش؛ فایل اصلی دست نمی‌خورد). پیش‌فرض خاموش.</summary>
    public bool ImageLogoOverlay { get; set; }

    /// <summary>دور ۲۹: جای لوگو روی عکس: «br»، «bl»، «tr»، «tl» یا «c» (وسط).</summary>
    public string ImageLogoPosition { get; set; } = "br";

    /// <summary>دور ۲۹: میزان پیدایی لوگو (۳ تا ۳۰ درصد؛ پیش‌فرض ۸ = خیلی کم‌رنگ).</summary>
    public int ImageLogoOpacity { get; set; } = 8;

    /// <summary>دور ۲۹: کدام لوگو: «mark» نشان برند (مربعی) یا «full» لوگوی کامل (افقی).</summary>
    public string ImageLogoKind { get; set; } = "mark";

    /// <summary>دور ۲۹: بازبینی و اصلاح خودکار دستهٔ کالاهای اشتباه (مثل «سرویس ۶ پارچه» در «چندراهی برق»). پیش‌فرض روشن.</summary>
    public bool Recategorize { get; set; } = true;

    // ── دور ۲۹: جایگاه بنرها ─────────────────────────────────────────

    /// <summary>دور ۲۹: حداکثر بنر فعال هم‌زمان در اسلایدر بالای صفحه (۱ تا ۱۰؛ پیش‌فرض ۵). بقیه به نوبت نمایش داده می‌شوند.</summary>
    public int BannerSlotsHero { get; set; } = 5;

    /// <summary>دور ۲۹: حداکثر بنر پهن میانهٔ صفحه (۱ تا ۶؛ پیش‌فرض ۲).</summary>
    public int BannerSlotsWide { get; set; } = 2;

    /// <summary>دور ۲۹: حداکثر بنر دوتایی/کارت تبلیغاتی (۱ تا ۴؛ پیش‌فرض ۲ = یک ردیف دوتایی؛ ۴ = دو ردیف).</summary>
    public int BannerSlotsPromo { get; set; } = 2;

    /// <summary>دور ۲۹: هر چند ساعت بنرهای اضافه (سنجاق‌نشده) جابه‌جا شوند (۱ تا ۷۲؛ پیش‌فرض ۶).</summary>
    public int BannerRotateHours { get; set; } = 6;

    // ── دور ۲۹: اعلان‌ها ─────────────────────────────────────────────

    /// <summary>دور ۲۹: اعلان‌های مشتری قدیمی‌تر از این چند روز کامل از پایگاه داده پاک شوند (۰ = خاموش؛ ۳۰ تا ۷۳۰). پیش‌فرض خاموش.</summary>
    public int NotificationPurgeDays { get; set; }

    /// <summary>دور ۲۹: اعلان‌های پنل مدیریت چند روز بعد از خوانده شدن خودکار پاک شوند (۰ = هرگز؛ ۱ تا ۹۰؛ پیش‌فرض ۷).</summary>
    public int StaffNotificationReadDays { get; set; } = 7;

    /// <summary>درصد تخفیف کد برگشت (۵ تا ۲۰؛ پیش‌فرض ۱۰).</summary>
    public int WinBackPercent { get; set; } = 10;

    /// <summary>سقف مبلغ تخفیف هر کد (تومان؛ ۵۰ هزار تا ۱ میلیون؛ پیش‌فرض ۲۰۰ هزار).</summary>
    public decimal WinBackMaxAmount { get; set; } = 200_000;

    /// <summary>اعتبار کد (روز؛ ۳ تا ۳۰؛ پیش‌فرض ۱۴).</summary>
    public int WinBackDays { get; set; } = 14;

    /// <summary>حداکثر پیام برگشت در روز (۱ تا ۲۰۰؛ پیش‌فرض ۳۰).</summary>
    public int WinBackPerDay { get; set; } = 30;


    /// <summary>
    /// مقدارهای خارج از بازه را اصلاح می‌کند تا یک عدد اشتباه در فرم، سایت را به خطر نیندازد.
    /// مثال: BlogPostsPerWeek = 50 ← ۷؛ MinImageSide = 10 ← ۳۰۰.
    /// </summary>
    public SmartOptions Normalize()
    {
        MinImageSide = Math.Clamp(MinImageSide, 300, 2000);
        MaxImageCandidates = Math.Clamp(MaxImageCandidates, 6, 60);
        AutoImagesWhenFewerThan = Math.Clamp(AutoImagesWhenFewerThan, 0, 12);
        AutoImagesMax = Math.Clamp(AutoImagesMax, 0, 8);
        ImageCandidates = Math.Clamp(ImageCandidates, 6, 15);
        if (OptionsVersion < CurrentVersion)
        {
            if (OptionsVersion < 23 && NightlyCompletePerDay == 20)
                NightlyCompletePerDay = 120;
            // دور ۲۹ (#۹): «حداقل ۵ عکس برای هر کالا» ← پیش‌فرض‌های قدیمی (۲ و ۳) یک بار به ۵ می‌رسند؛ عدد دلخواه مدیر دست نمی‌خورد.
            if (OptionsVersion < 29)
            {
                if (AutoImagesWhenFewerThan == 2)
                    AutoImagesWhenFewerThan = 5;
                if (AutoImagesMax == 3)
                    AutoImagesMax = 5;
            }
            OptionsVersion = CurrentVersion;
        }
        NightlyCompletePerDay = Math.Clamp(NightlyCompletePerDay, 0, 500);
        BannerIdeasPerDay = Math.Clamp(BannerIdeasPerDay, 0, 6);
        AutoSpecMinSources = Math.Clamp(AutoSpecMinSources, 2, 5);
        AutoApproveInsightMinReviews = Math.Clamp(AutoApproveInsightMinReviews, 3, 500);
        BlogPostsPerWeek = Math.Clamp(BlogPostsPerWeek, 0, 7);
        BlogQualityMin = Math.Clamp(BlogQualityMin, 50, 100);
        ReviewRequestAfterDays = Math.Clamp(ReviewRequestAfterDays, 1, 60);
        QuietFromHour = Math.Clamp(QuietFromHour, 0, 23);
        QuietToHour = Math.Clamp(QuietToHour, 0, 23);
        FavoritePriceDropPercent = Math.Clamp(FavoritePriceDropPercent, 3, 50);
        DeadStockWarnDays = Math.Clamp(DeadStockWarnDays, 14, 365);
        DeadStockCriticalDays = Math.Clamp(DeadStockCriticalDays, DeadStockWarnDays, 730);
        WinBackPercent = Math.Clamp(WinBackPercent, 5, 20);
        WinBackMaxAmount = Math.Clamp(WinBackMaxAmount, 50_000, 1_000_000);
        WinBackDays = Math.Clamp(WinBackDays, 3, 30);
        WinBackPerDay = Math.Clamp(WinBackPerDay, 1, 200);
        ReplenishRules = (ReplenishRules ?? [])
            .Where(r => r.CategoryId != Guid.Empty)
            .GroupBy(r => r.CategoryId).Select(g => g.Last())
            .Select(r => new ReplenishRule { CategoryId = r.CategoryId, Days = Math.Clamp(r.Days, 7, 730) })
            .Take(200).ToList();
        ReviewRequestTitle = Limit(ReviewRequestTitle, 150, "نظرتان دربارهٔ «{ProductName}» چیست؟");
        ReviewRequestBody = Limit(ReviewRequestBody, 600, "تجربهٔ شما به خریداران بعدی کمک می‌کند.");
        ReviewRequestSmsText = Limit(ReviewRequestSmsText, 300, "");
        ReplenishTitle = Limit(ReplenishTitle, 150, "وقت تهیهٔ دوبارهٔ «{ProductName}» است؟");
        ReplenishBody = Limit(ReplenishBody, 600, "اگر لازم دارید، دوباره سفارش دهید.");
        ReplenishSmsText = Limit(ReplenishSmsText, 300, "");
        AssistantName = Limit(AssistantName, 20, "آوا");
        // دور ۲۹: مقدارهای خارج از فهرست مجاز ← پیش‌فرض (هیچ متن دلخواهی به HTML/CSS صفحه نمی‌رسد).
        AssistantPosition = Pick(AssistantPosition, ["br", "bl", "mr", "ml"], "br");
        AssistantSubtitle = Limit(AssistantSubtitle, 40, "دستیار هوشمند فروشگاه · پاسخ خودکار");
        AssistantLogo = SafeLocalPath(AssistantLogo);
        AssistantColor = HexColor(AssistantColor, "#0f766e");
        MinProductImages = Math.Clamp(MinProductImages, 1, 12);
        ImageLogoPosition = Pick(ImageLogoPosition, ["br", "bl", "tr", "tl", "c"], "br");
        ImageLogoOpacity = Math.Clamp(ImageLogoOpacity, 3, 30);
        ImageLogoKind = Pick(ImageLogoKind, ["mark", "full"], "mark");
        BannerSlotsHero = Math.Clamp(BannerSlotsHero, 1, 10);
        BannerSlotsWide = Math.Clamp(BannerSlotsWide, 1, 6);
        BannerSlotsPromo = Math.Clamp(BannerSlotsPromo, 1, 4);
        BannerRotateHours = Math.Clamp(BannerRotateHours, 1, 72);
        NotificationPurgeDays = NotificationPurgeDays <= 0 ? 0 : Math.Clamp(NotificationPurgeDays, 30, 730);
        StaffNotificationReadDays = Math.Clamp(StaffNotificationReadDays, 0, 90);
        return this;
    }

    /// <summary>
    /// دور ۲۹: کپی مستقل. GetAsync نسخهٔ مشترک کش‌شده را برمی‌گرداند؛ هر جا قرار است تغییرش دهیم (فرم تنظیمات، پاک‌سازی اعلان‌ها)
    /// اول کپی می‌گیریم تا درخواست‌های هم‌زمان دیگر نسخهٔ نیمه‌کاره را نبینند.
    /// </summary>
    public SmartOptions Clone() =>
        JsonSerializer.Deserialize<SmartOptions>(JsonSerializer.Serialize(this, CloneJson), CloneJson) ?? new SmartOptions();

    private static readonly JsonSerializerOptions CloneJson = new(JsonSerializerDefaults.Web);

    /// <summary>دور ۲۹: فقط یکی از مقدارهای مجاز (کوچک‌شده)؛ بقیه ← پیش‌فرض. مثال: Pick("BR", ["br","bl"], "br") ← «br».</summary>
    private static string Pick(string? value, string[] allowed, string fallback)
    {
        var clean = (value ?? "").Trim().ToLowerInvariant();
        return Array.IndexOf(allowed, clean) >= 0 ? clean : fallback;
    }

    /// <summary>دور ۲۹: رنگ هگز ۶ رقمی با «#» (کوچک‌شده)؛ نامعتبر ← پیش‌فرض. مثال: «#0F766E» ← «#0f766e»؛ «red» ← پیش‌فرض.</summary>
    public static string HexColor(string? value, string fallback)
    {
        var clean = (value ?? "").Trim().ToLowerInvariant();
        if (clean.Length == 7 && clean[0] == '#' && clean.Skip(1).All(Uri.IsHexDigit))
            return clean;
        return fallback;
    }

    /// <summary>
    /// دور ۲۹: مسیر داخلی امن یک تصویر (فقط «/uploads/…» یا «/assets/…»، بدون «..»، فاصله، کوتیشن یا پروتکل)؛ بقیه ← null.
    /// مثال: «/uploads/media/a.webp» ← همان؛ «https://x/a.png» یا «//x/a.png» یا «/uploads/../web.config» ← null.
    /// </summary>
    public static string? SafeLocalPath(string? value)
    {
        var clean = value?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > 300 || clean.Contains("..", StringComparison.Ordinal))
            return null;
        if (!(clean.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) || clean.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)))
            return null;
        foreach (var c in clean)
        {
            if (char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '\\' or '(' or ')' or ';')
                return null;
        }
        return clean;
    }

    private static string Limit(string? text, int max, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        return value.Length > max ? value[..max] : value;
    }
}

/// <summary>
/// ساعت سکوت شبانه (C21). مثال: از ۲۲ تا ۹ ← ساعت ۲۳:۳۰ و ۸:۵۹ «سکوت» است، ۹:۰۰ و ۲۱:۵۹ نیست.
/// <para>کجا استفاده می‌شود؟ یادآوری سبد رهاشده (AbandonedCartService)، درخواست نظر و یادآوری تمدید (CustomerReminderService).</para>
/// </summary>
public static class CustomerQuietHours
{
    public static bool IsQuiet(DateTimeOffset now, int fromHour = 22, int toHour = 9)
    {
        var hour = IranClock.ToLocal(now).Hour;
        if (fromHour == toHour)
            return false;
        return fromHour > toHour
            ? hour >= fromHour || hour < toHour
            : hour >= fromHour && hour < toHour;
    }
}

/// <summary>
/// خواندن/ذخیرهٔ تنظیمات هوشمند (همه‌چیز روی خود سرور و با موتور داخلی؛ هیچ سرویس/مدل زبانی بیرونی در کار نیست).
/// <para>محل استفاده: صفحهٔ «تنظیمات محتوای هوشمند»، کارگر هوشمند، سرویس یادآوری‌ها، محافظ سود.</para>
/// </summary>
public interface ISmartConfigService
{
    /// <summary>تنظیمات فعلی (کش ۵ دقیقه‌ای؛ ردیف نبود ← پیش‌فرض‌ها).</summary>
    Task<SmartOptions> GetAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SaveAsync(SmartOptions options, CancellationToken cancellationToken = default);

}

public sealed class SmartConfigService(FratakShopDbContext context, IMemoryCache cache) : ISmartConfigService
{
    private const string CacheKey = "smart:options";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SmartOptions> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out SmartOptions? cached) && cached is not null)
            return cached;
        var json = await context.SmartContentConfigs.OrderBy(c => c.CreatedAt).Select(c => c.OptionsJson).FirstOrDefaultAsync(cancellationToken);
        var options = Parse(json);
        cache.Set(CacheKey, options, TimeSpan.FromMinutes(5));
        return options;
    }

    /// <summary>JSON تنظیمات ← شیء (خراب/خالی ← پیش‌فرض‌ها). عمومی برای آزمون‌ها.</summary>
    public static SmartOptions Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SmartOptions().Normalize();
        try
        {
            return (JsonSerializer.Deserialize<SmartOptions>(json, Json) ?? new SmartOptions()).Normalize();
        }
        catch (JsonException)
        {
            return new SmartOptions().Normalize();
        }
    }

    public async Task<OperationResult> SaveAsync(SmartOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var row = await RowAsync(cancellationToken);
        row.OptionsJson = JsonSerializer.Serialize(options.Normalize(), Json);
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);
        // دور ۲۹: بعد از پاک شدن کش تنظیمات، کش صفحه‌ها یک بار دیگر خالی می‌شود تا صفحه‌ای که در همین فاصله با تنظیم قبلی ساخته شد نماند.
        Fratak_Shop.Data.Utilities.DataChangeNotifier.Publish(typeof(SmartContentConfig));
        return OperationResult.Success("تنظیمات محتوای هوشمند ذخیره شد.");
    }

    private async Task<SmartContentConfig> RowAsync(CancellationToken cancellationToken)
    {
        var row = await context.SmartContentConfigs.AsTracking().OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (row is not null)
            return row;
        row = new SmartContentConfig { OptionsJson = JsonSerializer.Serialize(new SmartOptions().Normalize(), Json) };
        context.SmartContentConfigs.Add(row);
        return row;
    }
}
