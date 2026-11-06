using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// داده‌های پیش‌فرض مرحلهٔ دوازدهم: یک ردیف تنظیمات سرویس ایمیل در حالت «فقط لاگ» (هیچ ایمیل واقعی نمی‌رود تا
/// مدیر در پنل ← فنی ← سرویس ایمیل، SMTP را وصل و آزمایش کند). فقط وقتی جدول کاملاً خالی است اجرا می‌شود؛
/// پس تغییرات پنل با اجرای دوباره برنمی‌گردد و مدیر می‌تواند این ردیف را بعداً هر طور خواست عوض کند.
/// محل صدا زدن: <see cref="EngagementDefaultsSeeder"/>.
/// </summary>
public static class Round12DefaultsSeeder
{
    /// <summary>سقف پیش‌فرض ایمیل در ساعت (محافظت از حساب‌های رایگان مثل جیمیل که روزانه حدود ۵۰۰ ایمیل اجازه می‌دهند).</summary>
    public const int DefaultEmailMaxPerHour = 100;

    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await context.EmailProviderConfigs.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.EmailProviderConfigs.Add(new EmailProviderConfig
        {
            Mode = EmailMode.LogOnly,
            Port = 587,
            Security = EmailSecurity.Auto,
            MaxPerHour = DefaultEmailMaxPerHour,
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
