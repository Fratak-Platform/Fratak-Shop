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
    public int AutoImagesWhenFewerThan { get; set; } = 2;

    /// <summary>حداکثر عکسی که خودکار اضافه می‌شود (بقیه برای انتخاب کارشناس می‌ماند).</summary>
    public int AutoImagesMax { get; set; } = 3;

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
    /// دور بیستم: هر شب حداکثر چند محصول ناقص خودکار «تکمیل هوشمند» شوند (۰ = خاموش؛ پیش‌فرض ۲۰). محصولی که در ۳۰ روز گذشته
    /// تکمیل شده دوباره انتخاب نمی‌شود.
    /// </summary>
    public int NightlyCompletePerDay { get; set; } = 20;

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
        NightlyCompletePerDay = Math.Clamp(NightlyCompletePerDay, 0, 200);
        BannerIdeasPerDay = Math.Clamp(BannerIdeasPerDay, 0, 6);
        AutoSpecMinSources = Math.Clamp(AutoSpecMinSources, 2, 5);
        AutoApproveInsightMinReviews = Math.Clamp(AutoApproveInsightMinReviews, 3, 500);
        BlogPostsPerWeek = Math.Clamp(BlogPostsPerWeek, 0, 7);
        BlogQualityMin = Math.Clamp(BlogQualityMin, 50, 100);
        ReviewRequestAfterDays = Math.Clamp(ReviewRequestAfterDays, 1, 60);
        QuietFromHour = Math.Clamp(QuietFromHour, 0, 23);
        QuietToHour = Math.Clamp(QuietToHour, 0, 23);
        DeadStockWarnDays = Math.Clamp(DeadStockWarnDays, 14, 365);
        DeadStockCriticalDays = Math.Clamp(DeadStockCriticalDays, DeadStockWarnDays, 730);
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
        return this;
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
