using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Mappers.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Settings;

public class StoreSettingService(FratakShopDbContext context, IMemoryCache cache) : IStoreSettingService
{
    public async Task<StoreSettingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<StoreSettingDto>(CacheKeys.StoreSettings, out var cached) && cached is not null)
            return cached;

        var entity = await context.StoreSettings
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (entity is null)
        {
            // Normally created by the startup seeder; never let a missing row break the site.
            entity = new StoreSetting { SiteName = "فراتک شاپ" };
            context.StoreSettings.Add(entity);
            await context.SaveChangesAsync(cancellationToken);
        }

        var dto = entity.Map();
        cache.Set(CacheKeys.StoreSettings, dto, CacheKeys.DefaultLifetime);
        return dto;
    }

    public async Task<OperationResult> UpdateAsync(StoreSettingCommand command)
    {
        var error = Validate(command);
        if (error is not null)
            return OperationResult.Error(error);

        var entity = await context.StoreSettings.AsTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync();
        if (entity is null)
        {
            entity = new StoreSetting { SiteName = command.SiteName };
            context.StoreSettings.Add(entity);
        }

        command.SiteName = command.SiteName.Trim();
        command.ZarinPalMerchantId = string.IsNullOrWhiteSpace(command.ZarinPalMerchantId) ? null : command.ZarinPalMerchantId.Trim();
        command.TitleSeparator = string.IsNullOrWhiteSpace(command.TitleSeparator) ? "|" : command.TitleSeparator.Trim();
        entity.Apply(command);

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.StoreSettings);

        return OperationResult.Success("تنظیمات فروشگاه با موفقیت ذخیره شد.");
    }

    private static string? Validate(StoreSettingCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.SiteName))
            return "نام فروشگاه را وارد کنید.";

        if (command.FreeShippingThreshold < 0 || command.MinimumOrderAmount < 0 || command.CashOnDeliveryMaxAmount < 0)
            return "مبالغ نمی‌توانند منفی باشند.";

        if (command.WalletMinTopUpAmount < 1000 || command.WalletMaxTopUpAmount < command.WalletMinTopUpAmount)
            return "حداقل و حداکثر شارژ کیف پول معتبر نیست.";

        if (command.WalletMinWithdrawalAmount < 0)
            return "حداقل مبلغ برداشت معتبر نیست.";

        if (command.UnpaidOrderTimeoutMinutes is < 5 or > 10080)
            return "مهلت پرداخت سفارش باید بین ۵ دقیقه تا ۷ روز باشد.";

        if (command.DeliveryDaysAhead is < 1 or > 30)
            return "تعداد روزهای قابل انتخاب برای تحویل باید بین ۱ تا ۳۰ باشد.";

        if (command.SameDayLeadMinutes is < 0 or > 1440)
            return "فاصله زمانی تحویل همان روز باید بین ۰ تا ۱۴۴۰ دقیقه باشد.";

        if (!string.IsNullOrWhiteSpace(command.ZarinPalMerchantId) && !Guid.TryParse(command.ZarinPalMerchantId.Trim(), out _))
            return "مرچنت کد زرین‌پال باید یک شناسه ۳۶ کاراکتری باشد.";

        if (!string.IsNullOrWhiteSpace(command.SupportEmail) && !command.SupportEmail.Trim().IsValidEmail())
            return "ایمیل پشتیبانی معتبر نیست.";

        if (command.LoyaltyPointsExpireDays is < 1 or > 3650)
            return "مهلت اعتبار امتیازها باید بین ۱ تا ۳۶۵۰ روز باشد (یا خالی برای نامحدود).";

        if (command.WalletMaxWithdrawalAmount < 0 || command.WalletMaxPendingWithdrawals is < 1 or > 10)
            return "سقف برداشت معتبر نیست (تعداد درخواست‌های باز بین ۱ تا ۱۰).";

        if (command.WalletMaxWithdrawalAmount > 0 && command.WalletMaxWithdrawalAmount < command.WalletMinWithdrawalAmount)
            return "حداکثر مبلغ برداشت نمی‌تواند کمتر از حداقل آن باشد.";

        if (command.AbandonedCartAfterMinutes is < 10 or > 10080 || command.AbandonedCartExpireDays is < 1 or > 90 || command.AbandonedCartMinAmount < 0)
            return "تنظیمات سبدهای رها شده معتبر نیست (۱۰ دقیقه تا ۷ روز، پیگیری ۱ تا ۹۰ روز).";

        if (command.ReviewMaxImages is < 0 or > 10 || command.ReviewMaxImageSizeKb is < 100 or > 10240)
            return "تنظیمات تصاویر دیدگاه معتبر نیست (حداکثر ۱۰ تصویر، ۱۰۰ کیلوبایت تا ۱۰ مگابایت).";

        if (command.PwaInstallPromptAfterPages is < 0 or > 100 || command.PwaInstallPromptSnoozeDays is < 0 or > 365)
            return "تنظیمات پیشنهاد نصب اپلیکیشن معتبر نیست.";

        if (!string.IsNullOrWhiteSpace(command.InvoiceNumberPrefix)
            && !System.Text.RegularExpressions.Regex.IsMatch(command.InvoiceNumberPrefix.Trim(), "^[A-Za-z]{1,10}$"))
            return "پیشوند شماره فاکتور باید فقط حروف لاتین (حداکثر ۱۰ حرف) باشد.";

        var round5 = ValidateRound5(command);
        if (round5 is not null)
            return round5;

        if (!IsHexColor(command.ThemeColor) || !IsHexColor(command.AnnouncementColor))
            return "رنگ‌ها باید به صورت کد هگز باشند (مثلاً #0ea5e9).";

        if (!IsSiteLink(command.AnnouncementUrl) || !IsSiteLink(command.FooterButtonUrl))
            return "لینک‌ها باید با / شروع شوند یا آدرس کامل http(s) باشند.";

        if (!string.IsNullOrWhiteSpace(command.MapEmbedUrl) && !IsHttps(command.MapEmbedUrl))
            return "آدرس نقشه باید یک لینک https باشد.";

        var links = new[]
        {
            command.InstagramUrl, command.TelegramUrl, command.WhatsAppUrl, command.LinkedinUrl,
            command.TwitterUrl, command.AparatUrl, command.YoutubeUrl,
        };
        if (links.Any(link => !string.IsNullOrWhiteSpace(link) && !IsHttp(link)))
            return "لینک شبکه‌های اجتماعی باید با http:// یا https:// شروع شود.";

        return null;
    }

    /// <summary>Round 5 features (same ranges as the CK_StoreSettings_Round5 database check).</summary>
    private static string? ValidateRound5(StoreSettingCommand command)
    {
        if (command.ProductAlertMaxPerUser is < 1 or > 500)
            return "حداکثر «خبرم کن» هر کاربر باید بین ۱ تا ۵۰۰ باشد.";

        if (command.GiftCardMinAmount < 10_000 || command.GiftCardMaxAmount < command.GiftCardMinAmount || command.GiftCardMaxAmount > 1_000_000_000)
            return "حداقل و حداکثر مبلغ کارت هدیه معتبر نیست (حداقل ۱۰٬۰۰۰ تومان و حداکثر ≥ حداقل).";

        if (command.GiftCardValidDays is < 1 or > 3650)
            return "مدت اعتبار کارت هدیه باید بین ۱ تا ۳۶۵۰ روز باشد.";

        if (!string.IsNullOrWhiteSpace(command.GiftCardPresetAmounts)
            && !command.GiftCardPresetAmounts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(part => long.TryParse(part, out var value) && value > 0))
            return "مبالغ پیشنهادی کارت هدیه باید اعداد مثبت جدا شده با کاما باشند.";

        if (command.ReferralCookieDays is < 1 or > 365)
            return "مدت نگهداری کد دعوت باید بین ۱ تا ۳۶۵ روز باشد.";

        if (command.CaptchaAfterFailedAttempts is < 0 or > 20)
            return "تعداد تلاش ناموفق پیش از کپچا باید بین ۰ تا ۲۰ باشد.";

        if (command.PriceHistoryDays is < 7 or > 730)
            return "بازه نمودار تغییر قیمت باید بین ۷ تا ۷۳۰ روز باشد.";

        if (command.MapDefaultLatitude is < -90 or > 90 || command.MapDefaultLongitude is < -180 or > 180 || command.MapDefaultZoom is < 1 or > 19)
            return "موقعیت پیش‌فرض نقشه معتبر نیست.";

        if (!string.IsNullOrWhiteSpace(command.MapTileUrl)
            && (!command.MapTileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || !command.MapTileUrl.Contains("{z}") || !command.MapTileUrl.Contains("{x}") || !command.MapTileUrl.Contains("{y}")))
            return "آدرس کاشی نقشه باید https و شامل {z}، {x} و {y} باشد.";

        if (command.ReturnWindowDays is < 1 or > 365 || command.ReturnMaxImages is < 0 or > 10)
            return "تنظیمات مرجوعی معتبر نیست (مهلت ۱ تا ۳۶۵ روز، حداکثر ۱۰ تصویر).";

        if (command.OrderFeedbackWindowDays is < 1 or > 365)
            return "مهلت ثبت نظر درباره تجربه خرید باید بین ۱ تا ۳۶۵ روز باشد.";

        if (command.DefaultTheme is < 0 or > 2)
            return "پوسته پیش‌فرض معتبر نیست.";

        if (command.PopularSearchesCount is < 0 or > 30 || command.PopularSearchMinCount < 1)
            return "تنظیمات جست‌وجوهای پرتکرار معتبر نیست.";

        if (command.TicketMaxOpenPerUser is < 1 or > 50 || command.TicketMaxAttachments is < 0 or > 10 || command.TicketMaxAttachmentSizeKb is < 100 or > 10240)
            return "تنظیمات تیکت پشتیبانی معتبر نیست.";

        if (!string.IsNullOrWhiteSpace(command.VapidSubject)
            && !command.VapidSubject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            && !command.VapidSubject.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "شناسه VAPID باید با mailto: یا https:// شروع شود.";

        if (command.ImageQuality is < 40 or > 95)
            return "کیفیت تصاویر بهینه باید بین ۴۰ تا ۹۵ باشد.";

        if (command.PageCacheSeconds is < 10 or > 86400)
            return "مدت کش صفحات باید بین ۱۰ ثانیه تا ۱ روز باشد.";

        if (command.LowStockThreshold is < 0 or > 1000)
            return "آستانه موجودی کم باید بین ۰ تا ۱۰۰۰ باشد.";

        if (command.ErrorLogRetentionDays is < 1 or > 3650 || command.AuditLogRetentionDays is < 1 or > 3650)
            return "مدت نگهداری گزارش‌ها باید بین ۱ تا ۳۶۵۰ روز باشد.";

        // زمان‌بندی سفارش: هر مهلت باید منطقی باشد (حداقل ۱ ساعت، حداکثر ۳۰ روز).
        if (command.SlaSupplierOrderHours is < 1 or > 720 || command.SlaPreparationHours is < 1 or > 720
            || command.SlaTrackingCodeHours is < 1 or > 720)
            return "مهلت‌های زمان‌بندی سفارش باید بین ۱ تا ۷۲۰ ساعت باشند.";

        if (command.SlaDeliveryMinDays is < 0 or > 60 || command.SlaDeliveryMaxDays is < 1 or > 60
            || command.SlaDeliveryMinDays > command.SlaDeliveryMaxDays)
            return "روزهای تحویل باید بین ۰ تا ۶۰ باشند و حداقل از حداکثر بیشتر نباشد.";

        if (command.SlaWarningPercent is < 10 or > 95)
            return "درصد هشدار نزدیک شدن به پایان مهلت باید بین ۱۰ تا ۹۵ باشد.";

        if (command.PaymentLinkHours is < 1 or > 720)
            return "اعتبار لینک پرداخت باید بین ۱ تا ۷۲۰ ساعت باشد.";

        if (command.CodMinDeliveredOrders is < 0 or > 1000 || command.CodMaxReturnedOrders is < 0 or > 1000)
            return "شرط‌های پرداخت در محل باید بین ۰ تا ۱۰۰۰ باشند.";

        if (command.CodAllowedProvinces is { Length: > 1000 })
            return "فهرست استان‌های پرداخت در محل حداکثر ۱۰۰۰ نویسه است.";

        if (!string.IsNullOrWhiteSpace(command.XSiteHandle)
            && !System.Text.RegularExpressions.Regex.IsMatch(command.XSiteHandle.Trim(), "^@?[A-Za-z0-9_]{1,15}$"))
            return "نام کاربری ایکس (توییتر) معتبر نیست.";

        return null;
    }

    private static bool IsHexColor(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), "^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");

    /// <summary>Site-relative path ("/club", not "//evil") or an absolute http(s) URL.</summary>
    private static bool IsSiteLink(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (value.StartsWith('/') && !value.StartsWith("//") && !value.Contains('\\'))
        || IsHttp(value);

    private static bool IsHttps(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsHttp(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
