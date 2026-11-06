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
        // تنظیمات فنی (فاز ۹): فاصله‌های اضافه حذف و مقدار خالی null می‌شود تا «پیش‌فرض» معنا بدهد.
        command.BackupFolder = Blank(command.BackupFolder);
        command.MaintenanceMessage = Blank(command.MaintenanceMessage);
        command.MaintenanceAllowedIps = Blank(command.MaintenanceAllowedIps);
        command.HealthCheckToken = Blank(command.HealthCheckToken);
        // برند (مرحلهٔ دهم): خالی = پیش‌فرض برند.
        command.LogoUrl = Blank(command.LogoUrl);
        command.LogoDarkUrl = Blank(command.LogoDarkUrl);
        command.PwaIconUrl = Blank(command.PwaIconUrl);
        command.BrandTextPrimary = Blank(command.BrandTextPrimary);
        command.BrandTextAccent = Blank(command.BrandTextAccent);
        command.BrandTextColor = Blank(command.BrandTextColor);
        command.BrandAccentColor = Blank(command.BrandAccentColor);
        command.BrandStyleJson = Blank(command.BrandStyleJson);

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

        var round10 = ValidateRound10(command);
        if (round10 is not null)
            return round10;

        if (!IsHexColor(command.ThemeColor) || !IsHexColor(command.AnnouncementColor)
            || !IsHexColor(command.AnnouncementColor2) || !IsHexColor(command.AnnouncementTextColor))
            return "رنگ‌ها باید به صورت کد هگز باشند (مثلاً #0069FD).";

        // دور ۲۱: گزینه‌های ظاهر نوار اطلاعیه فقط از فهرست ثابت (عدد نامعتبر = دستکاری فرم).
        if (command.AnnouncementStyle is < 0 or > 4 || command.AnnouncementEffect is < 0 or > 4
            || command.AnnouncementFontSize is < 0 or > 2 || command.AnnouncementFontWeight is < 0 or > 2)
            return "گزینهٔ انتخاب‌شده برای ظاهر نوار اطلاعیه معتبر نیست.";
        if (command.AnnouncementMessages is { Length: > 1000 })
            return "پیام‌های چرخشی حداکثر ۱۰۰۰ نویسه است.";

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

        // بررسی خودکار تأمین‌کننده: بیش از ۲۴ بار در روز فقط فشار بیهوده به سایت منبع است.
        if (command.SupplierSyncRunsPerDay is < 1 or > 24)
            return "تعداد بررسی تأمین‌کننده در روز باید بین ۱ تا ۲۴ باشد.";
        if (command.SupplierStockMarginPercent is < 0 or > 90)
            return "حاشیهٔ اطمینان موجودی باید بین ۰ تا ۹۰ درصد باشد.";
        if (command.SupplierStockMaxQuantity is < 1 or > 100000)
            return "سقف موجودی پیشنهادی باید بین ۱ تا ۱۰۰٬۰۰۰ باشد.";
        if (command.SupplierRequestDelayMs is < 200 or > 30000)
            return "فاصلهٔ درخواست‌ها به تأمین‌کننده باید بین ۲۰۰ تا ۳۰٬۰۰۰ میلی‌ثانیه باشد.";

        // رهگیری پست: بیش از ۱۲ بار در روز فایده‌ای ندارد (سامانهٔ پست هم چند ساعت یک بار به‌روز می‌شود).
        if (command.PostTrackingRunsPerDay is < 1 or > 12)
            return "تعداد بررسی رهگیری پست در روز باید بین ۱ تا ۱۲ باشد.";
        if (command.PostTrackingMaxDays is < 3 or > 120)
            return "حداکثر روزهای رهگیری مرسوله باید بین ۳ تا ۱۲۰ باشد.";

        // آمار بازدید: کمتر از ۳۰ روز گزارش ماهانه را خراب می‌کند و بیش از ~۴ سال فقط جا می‌گیرد.
        if (command.AnalyticsRetentionDays is < 30 or > 1500)
            return "مدت نگهداری آمار بازدید باید بین ۳۰ تا ۱۵۰۰ روز باشد.";

        // موتور سئو: بیش از ۶ بار در روز فایده‌ای ندارد و فشار بی‌جا به منابع جست‌وجو می‌آورد.
        if (command.SeoEngineRunsPerDay is < 1 or > 6)
            return "تعداد اجرای موتور سئو در روز باید بین ۱ تا ۶ باشد.";
        if (command.SeoEngineBatchSize is < 5 or > 200)
            return "تعداد صفحه در هر اجرای موتور سئو باید بین ۵ تا ۲۰۰ باشد.";

        if (!string.IsNullOrWhiteSpace(command.XSiteHandle)
            && !System.Text.RegularExpressions.Regex.IsMatch(command.XSiteHandle.Trim(), "^@?[A-Za-z0-9_]{1,15}$"))
            return "نام کاربری ایکس (توییتر) معتبر نیست.";

        return ValidateOps(command);
    }

    /// <summary>
    /// اعتبارسنجی تنظیمات فنی (مرحلهٔ هفتم – فاز ۹). بازه‌ها با CK_StoreSettings_Ops در دیتابیس یکی است.
    /// مثال خطا: BackupRunsPerDay = 6 → «بین ۱ تا ۴»؛ IP مجاز «abc» → IP نامعتبر؛ توکن سلامت کوتاه‌تر از ۱۶ نویسه → خطا.
    /// </summary>
    private static string? ValidateOps(StoreSettingCommand command)
    {
        if (command.BackupRunsPerDay is < 1 or > 4)
            return "تعداد پشتیبان‌گیری در روز باید بین ۱ تا ۴ باشد.";
        if (command.BackupStartHour is < 0 or > 23)
            return "ساعت شروع پشتیبان‌گیری باید بین ۰ تا ۲۳ باشد.";
        if (command.BackupKeepCount is < 1 or > 60)
            return "تعداد پشتیبان‌های نگه‌داشته‌شده باید بین ۱ تا ۶۰ باشد.";
        if (command.BackupFolder is { } folder && (folder.Length > 400 || !Ops.BackupSchedule.IsValidFolder(folder)))
            return "مسیر پوشهٔ پشتیبان معتبر نیست؛ یک مسیر کامل روی سرور پایگاه داده بدهید (مثال: D:\\Backups یا /var/opt/mssql/backup).";

        if (command.MaintenanceRetryMinutes is < 5 or > 1440)
            return "زمان تخمینی پایان تعمیرات باید بین ۵ دقیقه تا ۲۴ ساعت باشد.";
        if (command.MaintenanceMessage is { Length: > 1000 })
            return "پیام حالت تعمیرات حداکثر ۱۰۰۰ نویسه است.";
        if (command.MaintenanceAllowedIps is { } ips)
        {
            if (ips.Length > 1000)
                return "فهرست IPهای مجاز حداکثر ۱۰۰۰ نویسه است.";
            var parsed = Ops.IpList.Parse(ips);
            if (parsed.Invalid.Count > 0)
                return "IP نامعتبر در فهرست مجاز حالت تعمیرات: " + string.Join("، ", parsed.Invalid.Take(3));
        }

        if (command.IpAutoBlockThreshold is < 5 or > 1000)
            return "آستانهٔ مسدودسازی خودکار باید بین ۵ تا ۱۰۰۰ ورود ناموفق باشد.";
        if (command.IpAutoBlockHours is < 1 or > 720)
            return "مدت مسدودسازی خودکار باید بین ۱ تا ۷۲۰ ساعت باشد.";
        if (command.LogFileRetentionDays is < 1 or > 180)
            return "مدت نگهداری فایل‌های گزارش باید بین ۱ تا ۱۸۰ روز باشد.";

        if (command.HealthCheckToken is { } token
            && (token.Length is < 16 or > 100 || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[A-Za-z0-9_\\-]+$")))
            return "توکن بررسی سلامت باید ۱۶ تا ۱۰۰ نویسه و فقط حروف انگلیسی، عدد، خط تیره یا زیرخط باشد.";

        return ValidateTeam(command);
    }

    /// <summary>
    /// بررسی تنظیمات مرحلهٔ دهم: برند (رنگ hex و آدرس امن آیکون‌ها)، سیاست قیمت تأمین‌کننده و هزینه‌های پیشنهاد قیمت.
    /// مثال خطا: BrandAccentColor = "blue" یا مجموع درصدهای کارمزد/هزینه/مالیات ۷۰٪.
    /// </summary>
    private static string? ValidateRound10(StoreSettingCommand command)
    {
        if (!IsHexColor(command.BrandTextColor) || !IsHexColor(command.BrandAccentColor))
            return "رنگ‌های متن برند باید کد هگز باشند (مثلاً #0069FD).";
        if (!IsSiteLink(command.LogoUrl) || !IsSiteLink(command.LogoDarkUrl) || !IsSiteLink(command.PwaIconUrl) || !IsSiteLink(command.FaviconUrl))
            return "آدرس آیکون‌ها باید با / شروع شود (فایل آپلودشده در کتابخانهٔ رسانه) یا آدرس کامل http(s) باشد.";
        if (command.BrandTextPrimary is { Length: > 40 } || command.BrandTextAccent is { Length: > 40 })
            return "هر بخش متن برند حداکثر ۴۰ نویسه است.";
        if ((command.BrandTextPrimary + command.BrandTextAccent).IndexOfAny(['<', '>']) >= 0)
            return "متن برند نباید < یا > داشته باشد.";

        // مرحلهٔ دوازدهم: استودیوی لوگو. JSON دوباره خوانده، بررسی و فشرده نوشته می‌شود (کلید ناشناخته یا مقدار خارج از محدوده رد می‌شود).
        if (command.BrandStyleJson is { } brandJson)
        {
            if (brandJson.Length > Branding.BrandStyleRules.MaxJsonLength)
                return "تنظیمات استودیوی لوگو بیش از حد بزرگ است.";
            if (!IsJsonObject(brandJson))
                return "تنظیمات استودیوی لوگو خراب است؛ صفحه را دوباره باز کنید.";
            var brandStyle = Branding.BrandStyleRules.Parse(brandJson);
            if (Branding.BrandStyleRules.Validate(brandStyle) is { } brandError)
                return brandError;
            command.BrandStyleJson = Branding.BrandStyleRules.Serialize(brandStyle);
        }

        if (command.SupplierPriceAutoMode is < 0 or > 2 || command.SupplierRepriceMode is < 0 or > 5)
            return "حالت قیمت‌گذاری خودکار تأمین‌کننده معتبر نیست.";
        if (command.SupplierRepriceMarkupPercent is < 0 or > 1000 || command.SupplierRepriceMinMarginPercent is < 0 or > 500)
            return "درصدهای سود قیمت‌گذاری خودکار باید بین ۰ تا ۱۰۰۰ (حداقل سود تا ۵۰۰) باشند.";
        if (command.SupplierRepriceMarkupAmount is < 0 or > 10_000_000_000m || command.SupplierRepriceRounding is < 0 or > 10_000_000m)
            return "مبلغ سود ثابت یا گرد کردن قیمت معتبر نیست.";
        if (command.SupplierRepriceMaxJumpPercent is < 0 or > 1000)
            return "سقف افزایش خودکار باید بین ۰ تا ۱۰۰۰ درصد باشد.";

        if (command.PricingPackagingCost < 0 || command.PricingShippingCost < 0 || command.PricingOtherCostAmount < 0
            || command.PricingPackagingCost + command.PricingShippingCost + command.PricingOtherCostAmount > 10_000_000_000m)
            return "هزینه‌های ثابت پیشنهاد قیمت معتبر نیست.";
        if (command.PricingGatewayFeePercent is < 0 or > 20 || command.PricingOtherCostPercent is < 0 or > 50 || command.PricingTaxPercent is < 0 or > 30)
            return "درصد کارمزد درگاه (۰ تا ۲۰)، هزینه‌های دیگر (۰ تا ۵۰) یا مالیات (۰ تا ۳۰) معتبر نیست.";
        if (command.PricingGatewayFeePercent + command.PricingOtherCostPercent + command.PricingTaxPercent >= 60)
            return "مجموع درصدهای کارمزد، هزینه و مالیات باید کمتر از ۶۰٪ باشد.";
        if (command.PricingTargetMarginPercent is < 0 or > 500 || command.PricingMinMarginPercent is < 0 or > 500)
            return "درصد سود هدف و حداقل سود باید بین ۰ تا ۵۰۰ باشد.";
        if (command.PricingMinMarginPercent > command.PricingTargetMarginPercent)
            return "حداقل سود نمی‌تواند از سود هدف بیشتر باشد.";
        if (command.PricingUndercutPercent is < 0 or > 30 || command.PricingRounding is < 0 or > 10_000_000m)
            return "درصد ارزان‌تر از رقبا (۰ تا ۳۰) یا گرد کردن قیمت پیشنهادی معتبر نیست.";
        return null;
    }

    /// <summary>بررسی تنظیمات «امنیت کارکنان» (مرحلهٔ هفتم – فاز ۱۰). مثال خطا: ReauthMinutes = 1.</summary>
    private static string? ValidateTeam(StoreSettingCommand command)
    {
        if (command.ReauthMinutes is < 5 or > 240)
            return "اعتبار تأیید دوبارهٔ هویت باید بین ۵ تا ۲۴۰ دقیقه باشد.";
        if (command.TwoPersonWalletThreshold is < 0 or > 100_000_000_000m || command.TwoPersonWithdrawalThreshold is < 0 or > 100_000_000_000m)
            return "سقف‌های تأیید دونفره باید عددی بین ۰ تا ۱۰۰ میلیارد تومان باشند.";
        if (command.IsStaffTotpRequired && !command.IsStaffTotpEnabled)
            command.IsStaffTotpRequired = false;
        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>متن یک شیء JSON سالم است. مثال: «{}» → true؛ «{broken» یا «[1]» → false.</summary>
    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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
