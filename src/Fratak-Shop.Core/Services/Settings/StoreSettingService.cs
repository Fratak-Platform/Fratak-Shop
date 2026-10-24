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
