using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// محتوای «نمونه» که فقط همراه دادهٔ نمونه (Shop:SeedDemoData = true) ساخته می‌شود — نه روی فروشگاه واقعی.
/// <para>چرا جدا شد (مرحلهٔ سیزدهم)؟ این‌ها قبلاً جزو دادهٔ پیش‌فرض بودند ولی روی سایت واقعی مشکل‌سازند:</para>
/// <list type="bullet">
/// <item>کدهای تخفیف عمومی (WELCOME10، FRATAK8، FREESHIP) و اعلان‌های تبلیغاتی که از آن‌ها نام می‌برند ← ضرر مالی ناخواسته.</item>
/// <item>تلفن، آدرس و نقشهٔ ساختگی ← اطلاعات غلط جلوی مشتری.</item>
/// <item>تصویر نمونهٔ نماد اعتماد الکترونیکی ← نماد اعتماد باید نماد واقعی همان فروشگاه با لینک اینماد باشد.</item>
/// <item>نرخ‌های نمونهٔ ارسال استانی (کیش، ابوموسی…) ← قیمت ارسال را مدیر خودش تعیین می‌کند.</item>
/// </list>
/// <para>محل صدا زدن: Program.cs (پروژهٔ Web)، کنار DemoDataSeeder، فقط وقتی دادهٔ نمونه واقعاً ساخته می‌شود.
/// هر بخش فقط وقتی جدولش خالی است کار می‌کند (اجرای دوباره چیزی را تکراری نمی‌سازد).</para>
/// </summary>
public static class SampleContentSeeder
{
    /// <summary>
    /// ساخت همهٔ محتوای نمونه. مثال: <c>await SampleContentSeeder.SeedAsync(context);</c>
    /// </summary>
    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await FillSampleContactAsync(context, cancellationToken);
        await EngagementDefaultsSeeder.SeedCouponsAsync(context, cancellationToken);
        await EngagementDefaultsSeeder.SeedBroadcastsAsync(context, cancellationToken);
        await EngagementDefaultsSeeder.SeedShippingRatesAsync(context, cancellationToken);
        await StoreDefaultsSeeder.SeedTrustBadgesAsync(context, cancellationToken);
    }

    /// <summary>
    /// اطلاعات تماس نمونه برای محیط توسعه (فقط فیلدهای خالی پر می‌شوند؛ مقدار واردشدهٔ مدیر دست نمی‌خورد)
    /// و متن نوار اطلاعیه با کد تخفیف نمونه (فقط اگر هنوز همان متن پیش‌فرض باشد).
    /// </summary>
    private static async Task FillSampleContactAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.StoreSettings.AsTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
            return;

        settings.SupportPhone ??= "021-91000000";
        settings.SupportEmail ??= "support@example.com";
        settings.Address ??= "تهران، خیابان ولیعصر، پلاک ۱۲۳ (نشانی نمونه)";
        settings.InstagramUrl ??= "https://instagram.com/";
        settings.TelegramUrl ??= "https://t.me/";
        if (settings.AnnouncementText == EngagementDefaultsSeeder.DefaultAnnouncementText)
        {
            settings.AnnouncementText = "🎁 ۱۰٪ تخفیف اولین خرید با کد WELCOME10 | عضویت رایگان در باشگاه مشتریان";
            settings.AnnouncementUrl = "/club";
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
