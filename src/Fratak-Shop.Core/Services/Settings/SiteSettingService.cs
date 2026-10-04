using System.Text.Json;
using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Settings;

public class SiteSettingService(FratakShopDbContext context) : ISiteSettingService
{
    /// <summary>عنوان فارسی هر گروه از تنظیمات (تب‌های صفحه تنظیمات).</summary>
    private static readonly Dictionary<string, string> GroupTitles = new()
    {
        ["General"] = "عمومی فروشگاه",
        ["Header"] = "هدر و نوار بالا",
        ["Footer"] = "فوتر و نمادها",
        ["Shop"] = "فروش و محصولات",
        ["Payment"] = "پرداخت و کارت به کارت",
        ["Shipping"] = "ارسال و مرسولات",
        ["Seo"] = "سئو و ابزارهای وبمستر",
        ["Social"] = "شبکه‌های اجتماعی",
        ["Notification"] = "پیام‌ها و اعلان‌ها",
        ["Appearance"] = "ظاهر و رنگ‌بندی",
    };

    public async Task<List<SiteSettingDto>> GetAllAsync(string? group = null)
    {
        var query = context.SiteSettings.AsQueryable();
        if (!string.IsNullOrWhiteSpace(group))
            query = query.Where(setting => setting.Group == group);

        var settings = await query
            .OrderBy(setting => setting.DisplayOrder)
            .ThenBy(setting => setting.Key)
            .ToListAsync();

        return settings.Select(Map).ToList();
    }

    public async Task<List<SettingGroupDto>> GetGroupedAsync()
    {
        var settings = await context.SiteSettings
            .OrderBy(setting => setting.DisplayOrder)
            .ThenBy(setting => setting.Key)
            .ToListAsync();

        return settings
            .GroupBy(setting => setting.Group ?? "General")
            .Select(group => new SettingGroupDto
            {
                Group = group.Key,
                Title = GroupTitles.TryGetValue(group.Key, out var title) ? title : group.Key,
                Settings = group.Select(Map).ToList(),
            })
            .OrderBy(group => GroupTitles.Keys.ToList().IndexOf(group.Group))
            .ToList();
    }

    public async Task<SiteSettingDto?> GetAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var setting = await context.SiteSettings.FirstOrDefaultAsync(item => item.Key == key);
        return setting is null ? null : Map(setting);
    }

    public async Task<string?> GetValueAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        return await context.SiteSettings
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync();
    }

    public async Task<T> GetValueAsync<T>(string key, T fallback)
    {
        var value = await GetValueAsync(key);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        try
        {
            if (typeof(T) == typeof(string))
                return (T)(object)value;

            if (typeof(T) == typeof(bool))
                return (T)(object)(value is "true" or "1" or "on" or "True" or "yes");

            if (typeof(T) == typeof(decimal))
                return (T)(object)value.ToEnglishDigits();

            if (typeof(T) == typeof(int))
                return (T)(object)(int)value.ToEnglishDigits();

            var converted = JsonSerializer.Deserialize<T>(value);
            return converted ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public async Task<StorefrontSettings> GetStorefrontSettingsAsync()
    {
        var settings = await context.SiteSettings
            .Where(setting => setting.IsPublic)
            .Select(setting => new { setting.Key, setting.Value })
            .ToListAsync();

        var map = settings
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.First().Value ?? string.Empty);

        string? Value(string key) => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
        bool Flag(string key, bool fallback = false) => Value(key) is { } value ? value is "true" or "1" or "on" : fallback;
        decimal? Amount(string key) => Value(key) is { } value ? value.ToEnglishDigits() : null;

        return new StorefrontSettings
        {
            SiteName = Value("General.SiteName") ?? "فراتک",
            SiteUrl = (Value("General.SiteUrl") ?? "https://fratak.ir").TrimEnd('/'),
            SiteDescription = Value("General.SiteDescription"),
            LogoImageName = Value("Header.LogoImageName"),
            FaviconImageName = Value("Header.FaviconImageName"),
            FooterLogoImageName = Value("Footer.LogoImageName"),
            PhoneNumber = Value("General.PhoneNumber"),
            MobileNumber = Value("General.MobileNumber"),
            Email = Value("General.Email"),
            Address = Value("General.Address"),
            WorkingHours = Value("General.WorkingHours"),
            PostalCode = Value("General.PostalCode"),
            InstagramUrl = Value("Social.InstagramUrl"),
            TelegramUrl = Value("Social.TelegramUrl"),
            WhatsappUrl = Value("Social.WhatsappUrl"),
            MapUrl = Value("General.MapUrl"),
            FooterAbout = Value("Footer.AboutText"),
            Copyright = Value("Footer.Copyright"),
            EnamadCode = Value("Footer.EnamadCode"),
            SamandehiCode = Value("Footer.SamandehiCode"),
            TrustBadgesJson = Value("Footer.TrustBadgesJson"),
            HeaderAnnouncement = Value("Header.AnnouncementText"),
            HeaderAnnouncementEnabled = Flag("Header.AnnouncementEnabled"),
            ShowHeaderTopBar = Flag("Header.ShowTopBar", true),
            ShowSocialInHeader = Flag("Header.ShowSocialLinks"),
            ShowSocialInFooter = Flag("Footer.ShowSocialLinks", true),
            FooterColumnsJson = Value("Footer.ColumnsJson"),
            PrimaryColor = Value("Appearance.PrimaryColor") ?? "#0ea5e9",
            DefaultShareImageName = Value("Seo.DefaultShareImageName"),
            EnableOnlinePayment = Flag("Payment.EnableOnlineGateway", true),
            EnableBankTransfer = Flag("Payment.EnableBankTransfer", true),
            EnableCashOnDelivery = Flag("Payment.EnableCashOnDelivery", true),
            FreeShippingThreshold = Amount("Shipping.FreeShippingThreshold"),
            PaymentInstructions = Value("Payment.Instructions"),
            BankTransferDeadlineHours = Value("Payment.BankTransferDeadlineHours"),
            ShowOutOfStock = Flag("Shop.ShowOutOfStockProducts", true),
            ShowPricesToGuests = Flag("Shop.ShowPricesToGuests", true),
            DefaultMetaDescription = Value("Seo.DefaultMetaDescription"),
            DefaultMetaKeywords = Value("Seo.DefaultMetaKeywords"),
            GoogleSiteVerification = Value("Seo.GoogleSiteVerification"),
            GoogleAnalyticsId = Value("Seo.GoogleAnalyticsId"),
            GoogleTagManagerId = Value("Seo.GoogleTagManagerId"),
            SearchConsoleUrl = Value("Seo.SearchConsoleUrl"),
            RobotsExtra = Value("Seo.RobotsExtra"),
            ShowVerificationCodeInDevelopment = Flag("General.ShowVerificationCodeInDevelopment", true),
            AboutShortText = Value("Footer.AboutShortText"),
        };
    }

    public async Task<OperationResult> SetAsync(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return OperationResult.Error("کلید تنظیمات نامعتبر است.");

        var setting = await context.SiteSettings.AsTracking().FirstOrDefaultAsync(item => item.Key == key);
        if (setting is null)
            return OperationResult.NotFound($"تنظیمات «{key}» یافت نشد.");

        setting.Value = value;
        await context.SaveChangesAsync();

        return OperationResult.Success("تنظیمات ذخیره شد.");
    }

    public async Task<OperationResult> SetManyAsync(IDictionary<string, string?> values)
    {
        if (values.Count == 0)
            return OperationResult.Error("موردی برای ذخیره ارسال نشده است.");

        var keys = values.Keys.ToList();
        var settings = await context.SiteSettings
            .AsTracking()
            .Where(setting => keys.Contains(setting.Key))
            .ToListAsync();

        foreach (var setting in settings)
        {
            if (values.TryGetValue(setting.Key, out var value))
                setting.Value = value;
        }

        var unknown = keys.Except(settings.Select(setting => setting.Key)).ToList();
        if (unknown.Count > 0)
            return OperationResult.Error($"تنظیمات ناشناخته: {string.Join(", ", unknown)}");

        await context.SaveChangesAsync();
        return OperationResult.Success("تنظیمات با موفقیت ذخیره شد.");
    }

    private static SiteSettingDto Map(SiteSetting setting) => new()
    {
        Id = setting.Id,
        Key = setting.Key,
        Value = setting.Value,
        Group = setting.Group,
        Title = setting.Title,
        Description = setting.Description,
        ValueType = setting.ValueType,
        Options = setting.Options,
        IsPublic = setting.IsPublic,
        DisplayOrder = setting.DisplayOrder,
        CreatedAt = setting.CreatedAt,
        UpdatedAt = setting.UpdatedAt,
        SelectOptions = ParseOptions(setting.Options),
    };

    private static List<SettingOptionDto> ParseOptions(string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<SettingOptionDto>>(options) ?? [];
        }
        catch (JsonException)
        {
            // «value|text,value|text» شکل ساده و سریع تعریف گزینه‌ها
            return options.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('|'))
                .Where(parts => parts.Length == 2)
                .Select(parts => new SettingOptionDto { Value = parts[0].Trim(), Text = parts[1].Trim() })
                .ToList();
        }
    }
    /// <summary>
    /// Seeds the settings of the shop on the first run. Every key used by the
    /// storefront (header, footer, payment, shipping, SEO) starts with a sensible
    /// Persian default, so no screen of the site is ever empty or hard-coded.
    /// </summary>
    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.SiteSettings
            .Select(setting => setting.Key)
            .ToListAsync(cancellationToken);

        var defaults = DefaultSettings()
            .Where(setting => !existing.Contains(setting.Key))
            .ToList();

        if (defaults.Count == 0)
            return;

        context.SiteSettings.AddRange(defaults);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static List<SiteSetting> DefaultSettings() =>
    [
        // ------------------------------------------------------------- عمومی
        new() { Key = "General.SiteName", Value = "فراتک", Group = "General", ValueType = SettingValueType.Text, Title = "نام فروشگاه", DisplayOrder = 1, IsPublic = true },
        new() { Key = "General.SiteUrl", Value = "https://fratak.ir", Group = "General", ValueType = SettingValueType.Text, Title = "آدرس سایت", Description = "در نقشه سایت و آدرس‌های کانونیکال استفاده می‌شود.", DisplayOrder = 2, IsPublic = true },
        new() { Key = "General.SiteDescription", Value = "فروشگاه اینترنتی فراتک؛ خرید آنلاین کالای اصل با ارسال سریع", Group = "General", ValueType = SettingValueType.Textarea, Title = "توضیح کوتاه سایت", DisplayOrder = 3, IsPublic = true },
        new() { Key = "General.PhoneNumber", Value = "021-00000000", Group = "General", ValueType = SettingValueType.Text, Title = "تلفن ثابت", DisplayOrder = 4, IsPublic = true },
        new() { Key = "General.MobileNumber", Value = "09120000000", Group = "General", ValueType = SettingValueType.Text, Title = "موبایل پشتیبانی", DisplayOrder = 5, IsPublic = true },
        new() { Key = "General.Email", Value = "info@fratak.ir", Group = "General", ValueType = SettingValueType.Text, Title = "ایمیل", DisplayOrder = 6, IsPublic = true },
        new() { Key = "General.Address", Value = "تهران، خیابان ولیعصر، پلاک ۱۲", Group = "General", ValueType = SettingValueType.Textarea, Title = "نشانی", DisplayOrder = 7, IsPublic = true },
        new() { Key = "General.WorkingHours", Value = "شنبه تا پنجشنبه ۹ تا ۱۸", Group = "General", ValueType = SettingValueType.Text, Title = "ساعات کاری", DisplayOrder = 8, IsPublic = true },
        new() { Key = "General.PostalCode", Value = "1234567890", Group = "General", ValueType = SettingValueType.Text, Title = "کد پستی", DisplayOrder = 9, IsPublic = true },
        new() { Key = "General.MapUrl", Value = "", Group = "General", ValueType = SettingValueType.Text, Title = "لینک نقشه", DisplayOrder = 10, IsPublic = true },
        new() { Key = "General.ShowVerificationCodeInDevelopment", Value = "true", Group = "General", ValueType = SettingValueType.Bool, Title = "نمایش کد تأیید در حالت توسعه", Description = "تا زمان اتصال سرویس پیامک، کد تأیید در صفحه نمایش داده می‌شود.", DisplayOrder = 11, IsPublic = false },

        // --------------------------------------------------------------- هدر
        new() { Key = "Header.LogoImageName", Value = "", Group = "Header", ValueType = SettingValueType.Image, Title = "لوگوی هدر", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Header.FaviconImageName", Value = "", Group = "Header", ValueType = SettingValueType.Image, Title = "فاوآیکون", DisplayOrder = 2, IsPublic = true },
        new() { Key = "Header.ShowTopBar", Value = "true", Group = "Header", ValueType = SettingValueType.Bool, Title = "نمایش نوار بالای هدر", DisplayOrder = 3, IsPublic = true },
        new() { Key = "Header.AnnouncementEnabled", Value = "true", Group = "Header", ValueType = SettingValueType.Bool, Title = "نمایش پیام اطلاع‌رسانی", DisplayOrder = 4, IsPublic = true },
        new() { Key = "Header.AnnouncementText", Value = "ارسال رایگان برای خریدهای بالای ۵ میلیون تومان", Group = "Header", ValueType = SettingValueType.Text, Title = "متن پیام هدر", DisplayOrder = 5, IsPublic = true },
        new() { Key = "Header.ShowSocialLinks", Value = "true", Group = "Header", ValueType = SettingValueType.Bool, Title = "نمایش شبکه‌های اجتماعی در هدر", DisplayOrder = 6, IsPublic = true },

        // -------------------------------------------------------------- فوتر
        new() { Key = "Footer.LogoImageName", Value = "", Group = "Footer", ValueType = SettingValueType.Image, Title = "لوگوی فوتر", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Footer.AboutText", Value = "فراتک یک فروشگاه اینترنتی تخصصی است که با تمرکز بر اصالت کالا، قیمت منصفانه و پشتیبانی واقعی فعالیت می‌کند.", Group = "Footer", ValueType = SettingValueType.Textarea, Title = "متن معرفی فوتر", DisplayOrder = 2, IsPublic = true },
        new() { Key = "Footer.AboutShortText", Value = "خرید مطمئن، ارسال سریع و پشتیبانی واقعی.", Group = "Footer", ValueType = SettingValueType.Text, Title = "متن کوتاه فوتر", DisplayOrder = 3, IsPublic = true },
        new() { Key = "Footer.Copyright", Value = "تمامی حقوق این سایت متعلق به فروشگاه فراتک است.", Group = "Footer", ValueType = SettingValueType.Text, Title = "متن کپی‌رایت", DisplayOrder = 4, IsPublic = true },
        new() { Key = "Footer.ShowSocialLinks", Value = "true", Group = "Footer", ValueType = SettingValueType.Bool, Title = "نمایش شبکه‌های اجتماعی در فوتر", DisplayOrder = 5, IsPublic = true },
        new() { Key = "Footer.EnamadCode", Value = "", Group = "Footer", ValueType = SettingValueType.Html, Title = "کد نماد اعتماد", DisplayOrder = 6, IsPublic = true },
        new() { Key = "Footer.SamandehiCode", Value = "", Group = "Footer", ValueType = SettingValueType.Html, Title = "کد ساماندهی", DisplayOrder = 7, IsPublic = true },
        new() { Key = "Footer.TrustBadgesJson", Value = "", Group = "Footer", ValueType = SettingValueType.Json, Title = "نشان‌های اعتماد", DisplayOrder = 8, IsPublic = true },

        // ------------------------------------------------------------- ظاهر
        new() { Key = "Appearance.PrimaryColor", Value = "#0ea5e9", Group = "Appearance", ValueType = SettingValueType.Color, Title = "رنگ اصلی سایت", DisplayOrder = 1, IsPublic = true },

        // -------------------------------------------------------- شبکه اجتماعی
        new() { Key = "Social.InstagramUrl", Value = "", Group = "Social", ValueType = SettingValueType.Text, Title = "آدرس اینستاگرام", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Social.TelegramUrl", Value = "", Group = "Social", ValueType = SettingValueType.Text, Title = "آدرس تلگرام", DisplayOrder = 2, IsPublic = true },
        new() { Key = "Social.WhatsappUrl", Value = "", Group = "Social", ValueType = SettingValueType.Text, Title = "آدرس واتس‌اپ", DisplayOrder = 3, IsPublic = true },

        // ------------------------------------------------------------ فروشگاه
        new() { Key = "Shop.ShowOutOfStockProducts", Value = "true", Group = "Shop", ValueType = SettingValueType.Bool, Title = "نمایش کالاهای ناموجود", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Shop.ShowPricesToGuests", Value = "true", Group = "Shop", ValueType = SettingValueType.Bool, Title = "نمایش قیمت به مهمان‌ها", DisplayOrder = 2, IsPublic = true },

        // -------------------------------------------------------------- پرداخت
        new() { Key = "Payment.EnableOnlineGateway", Value = "true", Group = "Payment", ValueType = SettingValueType.Bool, Title = "فعال بودن پرداخت آنلاین (سندباکس زرین‌پال)", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Payment.ZarinPalMerchantId", Value = "", Group = "Payment", ValueType = SettingValueType.Text, Title = "شناسه پذیرنده زرین‌پال سندباکس", Description = "شناسه پذیرنده سندباکس زرین‌پال را وارد کنید؛ در صورت خالی بودن، حالت شبیه‌سازی داخلی فعال می‌ماند.", DisplayOrder = 2, IsPublic = false },
        new() { Key = "Payment.EnableBankTransfer", Value = "true", Group = "Payment", ValueType = SettingValueType.Bool, Title = "فعال بودن کارت به کارت", DisplayOrder = 3, IsPublic = true },
        new() { Key = "Payment.EnableCashOnDelivery", Value = "true", Group = "Payment", ValueType = SettingValueType.Bool, Title = "فعال بودن پرداخت در محل", DisplayOrder = 3, IsPublic = true },
        new() { Key = "Payment.BankTransferDeadlineHours", Value = "24", Group = "Payment", ValueType = SettingValueType.Number, Title = "مهلت پرداخت کارت به کارت (ساعت)", DisplayOrder = 4, IsPublic = true },
        new() { Key = "Payment.Instructions", Value = "پس از واریز، اطلاعات و تصویر فیش را در همین صفحه ثبت کنید تا کارشناسان ما در سریع‌ترین زمان بررسی کنند.", Group = "Payment", ValueType = SettingValueType.Textarea, Title = "توضیحات پرداخت", DisplayOrder = 5, IsPublic = true },

        // --------------------------------------------------------------- ارسال
        new() { Key = "Shipping.FreeShippingThreshold", Value = "5000000", Group = "Shipping", ValueType = SettingValueType.Number, Title = "سقف ارسال رایگان (تومان)", DisplayOrder = 1, IsPublic = true },

        // ----------------------------------------------------------------- سئو
        new() { Key = "Seo.DefaultMetaDescription", Value = "فروشگاه اینترنتی فراتک؛ خرید آنلاین کالای دیجیتال با ضمانت اصالت، ارسال سریع و پشتیبانی تخصصی.", Group = "Seo", ValueType = SettingValueType.Textarea, Title = "توضیحات پیش‌فرض متا", DisplayOrder = 1, IsPublic = true },
        new() { Key = "Seo.DefaultMetaKeywords", Value = "فروشگاه اینترنتی, خرید آنلاین, فراتک", Group = "Seo", ValueType = SettingValueType.Text, Title = "کلمات کلیدی پیش‌فرض", DisplayOrder = 2, IsPublic = true },
        new() { Key = "Seo.DefaultShareImageName", Value = "", Group = "Seo", ValueType = SettingValueType.Image, Title = "تصویر پیش‌فرض اشتراک‌گذاری", DisplayOrder = 3, IsPublic = true },
        new() { Key = "Seo.GoogleSiteVerification", Value = "", Group = "Seo", ValueType = SettingValueType.Text, Title = "کد تأیید گوگل سرچ کنسول", DisplayOrder = 4, IsPublic = true },
        new() { Key = "Seo.GoogleAnalyticsId", Value = "", Group = "Seo", ValueType = SettingValueType.Text, Title = "شناسه گوگل آنالیتیکس", DisplayOrder = 5, IsPublic = true },
        new() { Key = "Seo.GoogleTagManagerId", Value = "", Group = "Seo", ValueType = SettingValueType.Text, Title = "شناسه گوگل تگ منیجر", DisplayOrder = 6, IsPublic = true },
        new() { Key = "Seo.SearchConsoleUrl", Value = "https://search.google.com/search-console", Group = "Seo", ValueType = SettingValueType.Text, Title = "لینک سرچ کنسول", DisplayOrder = 7, IsPublic = true },
        new() { Key = "Seo.RobotsExtra", Value = "", Group = "Seo", ValueType = SettingValueType.Textarea, Title = "دستورات اضافی robots.txt", DisplayOrder = 8, IsPublic = true },

        // ------------------------------------------------------------- اعلان‌ها
        new() { Key = "Notification.SmsEnabled", Value = "false", Group = "Notification", ValueType = SettingValueType.Bool, Title = "ارسال پیامک فعال است", Description = "تا زمان اتصال سرویس پیامک، کدها فقط در اعلان‌های سایت ثبت می‌شوند.", DisplayOrder = 1, IsPublic = false },
    ];
}