using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// داده‌های پیش‌فرض دور هشتم. هر بخش فقط وقتی اجرا می‌شود که جدولش کاملاً خالی باشد؛ تغییرات پنل با اجرای دوباره برنمی‌گردد.
/// محل صدا زدن: <see cref="EngagementDefaultsSeeder"/>.
/// </summary>
public static class Round8DefaultsSeeder
{
    /// <summary>خط و قالب پیش‌فرض SMS.ir (از پنل قابل تغییر). کلید API عمداً اینجا نیست: راز در کد منبع نمی‌ماند.</summary>
    public const long DefaultSmsLineNumber = 30000123456;
    public const int DefaultSmsOtpTemplateId = 718643;
    public const string DefaultSmsOtpParameter = "Code";

    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (await context.SmsProviderConfigs.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.SmsProviderConfigs.Add(new SmsProviderConfig
        {
            Provider = SmsProvider.SmsIr,
            LineNumber = DefaultSmsLineNumber,
            OtpTemplateId = DefaultSmsOtpTemplateId,
            OtpParameterName = DefaultSmsOtpParameter,
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
