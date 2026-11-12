using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// نتیجهٔ آماده‌سازی دادهٔ پیش‌فرض در شروع برنامه (برای لاگ و آزمون‌ها).
/// </summary>
public enum DefaultDataOutcome
{
    /// <summary>پایگاه داده تازه بود و دادهٔ پیش‌فرض همین حالا ساخته شد.</summary>
    Seeded = 0,

    /// <summary>پایگاه داده تازه است ولی Shop:SeedDefaultData خاموش است؛ چیزی ساخته نشد.</summary>
    Disabled = 1,

    /// <summary>پایگاه داده قبلاً راه‌اندازی شده است؛ هیچ داده‌ای دوباره ساخته نمی‌شود (حتی اگر مدیر چیزی را حذف کرده باشد).</summary>
    AlreadyInitialized = 2,
}

/// <summary>
/// «دادهٔ پیش‌فرض سیستم» (نام فروشگاه، تنظیمات، صفحه‌های محتوایی، پرسش‌های متداول، منوها، بلوک‌های صفحهٔ اصلی،
/// قالب‌های اعلان، باشگاه مشتریان، پیشنهادهای جست‌وجو، پیام‌های آماده، راهنمای پنل و …) — فقط یک بار، روی پایگاه دادهٔ کاملاً تازه.
/// <para><b>قانون (مرحلهٔ سیزدهم، خواستهٔ صریح مالک):</b> وقتی Shop:SeedDefaultData = true است، این داده فقط بار اولی ساخته می‌شود
/// که پایگاه داده خالی است. در اجراهای بعدی هرگز دوباره ساخته نمی‌شود؛ حتی اگر مدیر مثلاً همهٔ پرسش‌های متداول را پاک کرده باشد.</para>
/// <para>«تازه بودن» یعنی: هنوز هیچ ردیف تنظیمات فروشگاه، هیچ محصول و هیچ دسته‌ای (حتی حذف‌شده) وجود ندارد.
/// ردیف تنظیمات در همان اجرای اول ساخته می‌شود و پاک‌شدنی نیست، پس خودش نشانهٔ «راه‌اندازی شده» است و جدول جداگانه‌ای لازم نیست.</para>
/// <para>همهٔ کار در یک تراکنش انجام می‌شود: اگر وسط کار برق برود یا خطایی رخ دهد، هیچ چیز نیمه‌کاره نمی‌ماند و
/// اجرای بعدی (که پایگاه داده هنوز تازه است) کار را از اول و کامل انجام می‌دهد.</para>
/// <para>محل صدا زدن: <see cref="Extensions.FratakShopBootstrapper.InitializeFratakShopDataAsync(IServiceProvider, bool, CancellationToken)"/>.</para>
/// </summary>
public static class DefaultDataSeeder
{
    /// <summary>
    /// آیا پایگاه داده هنوز هیچ دادهٔ فروشگاهی ندارد؟ (کاربران حساب نمی‌شوند: حساب مالک ممکن است زودتر ساخته شده باشد.)
    /// <para>مثال: پایگاه دادهٔ تازه بعد از مهاجرت‌ها ← true؛ فروشگاهی که یک بار بالا آمده ← false.</para>
    /// </summary>
    public static async Task<bool> IsFreshDatabaseAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return !await context.StoreSettings.IgnoreQueryFilters().AnyAsync(cancellationToken)
               && !await context.Products.IgnoreQueryFilters().AnyAsync(cancellationToken)
               && !await context.Categories.IgnoreQueryFilters().AnyAsync(cancellationToken);
    }

    /// <summary>
    /// اجرای قانون بالا. <paramref name="enabled"/> همان مقدار Shop:SeedDefaultData است.
    /// <para>مثال: اولین اجرا روی سرور با true ← <see cref="DefaultDataOutcome.Seeded"/>؛ اجرای دوم ← <see cref="DefaultDataOutcome.AlreadyInitialized"/>.</para>
    /// </summary>
    public static async Task<DefaultDataOutcome> EnsureAsync(FratakShopDbContext context, bool enabled, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!await IsFreshDatabaseAsync(context, cancellationToken))
            return DefaultDataOutcome.AlreadyInitialized;
        if (!enabled)
            return DefaultDataOutcome.Disabled;

        // استراتژی اجرای EF (اگر روزی تلاش دوباره روشن شود) کل تراکنش را با هم تکرار می‌کند.
        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await StoreDefaultsSeeder.SeedAsync(context, cancellationToken);
            await EngagementDefaultsSeeder.SeedAsync(context, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
        context.ChangeTracker.Clear();
        return DefaultDataOutcome.Seeded;
    }
}
