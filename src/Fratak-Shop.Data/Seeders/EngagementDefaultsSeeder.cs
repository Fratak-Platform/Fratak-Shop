using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Shipping;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// Default (sample) data of the newer, admin-managed features so they can be tried
/// before the admin panel exists: header/footer menus, listing filters, coupons,
/// customer-club rules / levels / rewards, notification texts and two broadcasts.
/// Like <see cref="StoreDefaultsSeeder"/>, every block only runs when its table is empty,
/// so later edits are never overwritten. Sample coupons and broadcasts can simply be
/// deleted (or deactivated) once real data exists.
/// </summary>
public static class EngagementDefaultsSeeder
{
    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await BackfillSettingsAsync(context, cancellationToken);
        await SeedMenusAsync(context, cancellationToken);
        await SeedPersonalHomeSectionsAsync(context, cancellationToken);
        await SeedFilterAttributesAsync(context, cancellationToken);
        await SeedCouponsAsync(context, cancellationToken);
        await SeedLoyaltyAsync(context, cancellationToken);
        await SeedNotificationTemplatesAsync(context, cancellationToken);
        await SeedBroadcastsAsync(context, cancellationToken);

        // Round 4
        await BackfillRound4SettingsAsync(context, cancellationToken);
        await UpdateLoyaltyTextsAsync(context, cancellationToken);
        await SeedShippingRatesAsync(context, cancellationToken);
        await SeedAbandonedCartStepsAsync(context, cancellationToken);
        await SeedPageAlertsAsync(context, cancellationToken);

        // Round 5
        await Round5DefaultsSeeder.SeedAsync(context, cancellationToken);

        // Round 7 (پیام‌های آماده و ...)
        await Round7DefaultsSeeder.SeedAsync(context, cancellationToken);

        // Round 8 (تنظیمات پیش‌فرض سرویس پیامک)
        await Round8DefaultsSeeder.SeedAsync(context, cancellationToken);

        // مرحلهٔ دوازدهم (تنظیمات پیش‌فرض سرویس ایمیل: «فقط لاگ» تا مدیر SMTP را وصل کند)
        await Round12DefaultsSeeder.SeedAsync(context, cancellationToken);

        // هویت بصری فراتک شاپ
        await ApplyBrandColorsAsync(context, cancellationToken);
    }

    /// <summary>
    /// سرمه‌ای برند (Visual-Identities/Fratak-Shop/tokens.css → --fr-navy). همان مقدار
    /// <c>BrandIdentity.Navy</c> در پروژهٔ Web؛ اینجا تکرار شده چون پروژهٔ Data به Web دسترسی ندارد.
    /// </summary>
    private const string BrandNavy = "#0B1927";

    /// <summary>
    /// انتقال نصب‌های قبلی به رنگ‌های هویت بصری: فقط اگر «رنگ اصلی» و «رنگ نوار اطلاعیه» هنوز همان
    /// مقدارهای پیش‌فرض قدیمی قالب (آبی آسمانی #0ea5e9 و #0369a1) باشند، با سرمه‌ای برند جایگزین می‌شوند.
    /// رنگی که مدیر خودش انتخاب کرده دست نمی‌خورد؛ اجرای دوباره هم کاری نمی‌کند (چون دیگر مقدار قدیمی نیست).
    /// محل صدا زدن: انتهای <see cref="SeedAsync"/>.
    /// </summary>
    private static async Task ApplyBrandColorsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.StoreSettings.AsTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
            return;

        var changed = false;
        if (string.Equals(settings.ThemeColor, "#0ea5e9", StringComparison.OrdinalIgnoreCase))
        {
            settings.ThemeColor = BrandNavy;
            changed = true;
        }

        if (string.Equals(settings.AnnouncementColor, "#0369a1", StringComparison.OrdinalIgnoreCase))
        {
            settings.AnnouncementColor = BrandNavy;
            changed = true;
        }

        if (changed)
            await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Texts of the round-4 settings (club description, install suggestion) – once, when still empty.</summary>
    private static async Task BackfillRound4SettingsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.StoreSettings.AsTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null || settings.PwaInstallPromptTitle is not null || settings.LoyaltyProgramDescription is not null)
            return;

        settings.LoyaltyProgramDescription = "با هر خرید و فعالیت در فروشگاه امتیاز بگیرید، به سطح‌های بالاتر برسید و امتیازها را با اعتبار خرید، کد تخفیف و ارسال رایگان عوض کنید. امتیاز خرید پس از تحویل سفارش به حساب شما اضافه می‌شود.";
        settings.LoyaltyReferralDescription = "لینک یا کد معرف خود را برای دوستانتان بفرستید؛ با ثبت‌نام و اولین خرید آن‌ها، هر دو امتیاز می‌گیرید.";
        settings.PwaInstallPromptTitle = "فراتک شاپ را نصب کنید";
        settings.PwaInstallPromptText = "دسترسی سریع‌تر از صفحه اصلی گوشی، بدون نیاز به دانلود از فروشگاه برنامه‌ها.";
        settings.InvoiceNumberPrefix ??= "INV";

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Purchase points are now given once the order is delivered (anti-abuse). Updates the default
    /// texts of existing installations – only rows that still have the original default text.
    /// </summary>
    private static async Task UpdateLoyaltyTextsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var updates = new (LoyaltyAction Action, string OldText, string NewText)[]
        {
            (LoyaltyAction.Purchase, "به ازای هر ۱۰ هزار تومان خرید، ۱ امتیاز (حداکثر ۲۰۰۰ امتیاز در هر سفارش)", PurchaseRuleText),
            (LoyaltyAction.FirstOrder, "امتیاز ویژه اولین سفارش پرداخت‌شده", FirstOrderRuleText),
        };

        foreach (var (action, oldText, newText) in updates)
        {
            await context.LoyaltyRules
                .Where(r => r.Action == action && r.Description == oldText)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Description, newText), cancellationToken);
        }
    }

    private const string PurchaseRuleText = "به ازای هر ۱۰ هزار تومان خرید، ۱ امتیاز (حداکثر ۲۰۰۰ امتیاز در هر سفارش) — پس از تحویل سفارش";
    private const string FirstOrderRuleText = "امتیاز ویژه اولین سفارش — پس از تحویل سفارش";

    /// <summary>
    /// Sample province / city shipping rules: post is priced by province (unlisted provinces keep
    /// the base cost), Kish has its own city price and Abu Musa is blocked for every method.
    /// Runs once, when the rules table is empty and the post method exists.
    /// </summary>
    private static async Task SeedShippingRatesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ShippingRates.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var post = await context.ShippingMethods.AsTracking().FirstOrDefaultAsync(m => m.Code == "post", cancellationToken);
        if (post is null)
            return;

        var locationIds = new[] { "123", "130", "111", "122", "1220003002193", "1220001001563" }
            .Select(LocationSeeder.ProvinceId)
            .ToList();
        var existing = await context.Locations.Where(l => locationIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(cancellationToken);
        if (existing.Count != locationIds.Count)
            return;

        post.PricingMode = ShippingPricingMode.ByLocation;

        context.ShippingRates.AddRange(
            new ShippingRate { ShippingMethodId = post.Id, ProvinceId = LocationSeeder.ProvinceId("123"), Cost = 45_000, Note = "تهران" },
            new ShippingRate { ShippingMethodId = post.Id, ProvinceId = LocationSeeder.ProvinceId("130"), Cost = 50_000, Note = "البرز" },
            new ShippingRate { ShippingMethodId = post.Id, ProvinceId = LocationSeeder.ProvinceId("111"), Cost = 85_000, ExtraDeliveryDays = 2, Note = "مناطق دوردست" },
            new ShippingRate { ShippingMethodId = post.Id, ProvinceId = LocationSeeder.ProvinceId("122"), Cost = 80_000, ExtraDeliveryDays = 1, Note = "هرمزگان" },
            new ShippingRate
            {
                ShippingMethodId = post.Id,
                ProvinceId = LocationSeeder.ProvinceId("122"),
                CityId = LocationSeeder.ProvinceId("1220003002193"),
                Cost = 95_000,
                ExtraDeliveryDays = 2,
                Note = "جزیره کیش",
            },
            new ShippingRate
            {
                ProvinceId = LocationSeeder.ProvinceId("122"),
                CityId = LocationSeeder.ProvinceId("1220001001563"),
                IsBlocked = true,
                Note = "ارسال به جزیره ابوموسی فعلاً امکان‌پذیر نیست",
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Default abandoned-cart reminders: a notice after 1 hour, a personal 5% code after 24 hours.</summary>
    private static async Task SeedAbandonedCartStepsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.AbandonedCartReminderSteps.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.AbandonedCartReminderSteps.AddRange(
            new AbandonedCartReminderStep
            {
                StepNumber = 1,
                DelayMinutes = 60,
                Channel = ReminderChannel.Notification,
                Title = "سبد خریدتان منتظر شماست",
                Message = "{Name} عزیز، {ItemCount} کالا به ارزش {CartAmount} تومان در سبد خرید شما مانده است. موجودی کالاها محدود است؛ همین حالا خرید را تکمیل کنید.",
                SmsText = "{Name} عزیز، سبد خرید شما در {SiteName} منتظر شماست.",
            },
            new AbandonedCartReminderStep
            {
                StepNumber = 2,
                DelayMinutes = 24 * 60,
                Channel = ReminderChannel.Notification,
                Title = "یک هدیه برای تکمیل خرید",
                Message = "{Name} عزیز، با کد اختصاصی {CouponCode} سفارش خود را با {CouponValue} تخفیف تکمیل کنید. این کد فقط تا چند روز معتبر است.",
                SmsText = "کد تخفیف اختصاصی شما در {SiteName}: {CouponCode}",
                IncludeCoupon = true,
                CouponType = CouponType.Percentage,
                CouponValue = 5,
                CouponMaxDiscountAmount = 200_000,
                CouponValidDays = 3,
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sample page alerts (product, cart, checkout, order pages). Runs once, when the table is empty.</summary>
    private static async Task SeedPageAlertsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.PageAlerts.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.PageAlerts.AddRange(
            new PageAlert
            {
                Page = SitePage.ProductDetails,
                Position = AlertPosition.BelowMainImage,
                Type = AlertType.Success,
                Title = "ضمانت اصالت کالا",
                Content = "همه کالاها اصل هستند و تا ۷ روز با حفظ شرایط اولیه قابل بازگشت‌اند.",
                LinkUrl = "/page/return-policy",
                LinkText = "شرایط بازگشت",
                DisplayOrder = 1,
            },
            new PageAlert
            {
                Page = SitePage.Cart,
                Position = AlertPosition.AboveContent,
                Type = AlertType.Info,
                Content = "با هر خرید امتیاز باشگاه مشتریان بگیرید؛ امتیاز پس از تحویل سفارش به حساب شما اضافه می‌شود.",
                LinkUrl = "/club",
                LinkText = "باشگاه مشتریان",
                IsDismissible = true,
                DisplayOrder = 1,
            },
            new PageAlert
            {
                Page = SitePage.Checkout,
                Position = AlertPosition.AboveContent,
                Type = AlertType.Warning,
                Content = "پیش از پرداخت، آدرس و شماره تماس گیرنده را بررسی کنید تا سفارش بدون تأخیر به دستتان برسد.",
                IsDismissible = true,
                DisplayOrder = 1,
            },
            new PageAlert
            {
                Page = SitePage.CheckoutComplete,
                Position = AlertPosition.AboveContent,
                Type = AlertType.Info,
                Content = "فاکتور سفارش پس از پرداخت از بخش «سفارش‌های من» قابل مشاهده، چاپ و دریافت PDF است.",
                LinkUrl = "/profile/orders",
                LinkText = "سفارش‌های من",
                Audience = AlertAudience.SignedIn,
                DisplayOrder = 1,
            },
            new PageAlert
            {
                Page = SitePage.OrderDetails,
                Position = AlertPosition.AboveContent,
                Type = AlertType.Info,
                Content = "تا پیش از شروع آماده‌سازی می‌توانید سفارش را لغو کنید؛ مبلغ پرداختی بلافاصله به کیف پول شما برمی‌گردد.",
                IsDismissible = true,
                DisplayOrder = 1,
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gives the settings row (also of existing installations) sensible values for the new
    /// fields once – detected by all of them still being empty.
    /// </summary>
    private static async Task BackfillSettingsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var settings = await context.StoreSettings.AsTracking().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (settings is null || settings.ThemeColor is not null || settings.PwaShortName is not null || settings.LoyaltyProgramName is not null)
            return;

        settings.ThemeColor = BrandNavy;
        settings.PwaShortName = "فراتک شاپ";
        settings.LoyaltyProgramName = "باشگاه مشتریان فراتک";
        settings.LoyaltyPointsExpireDays = 365;
        settings.FooterButtonText ??= "مشاهده محصولات";
        settings.FooterButtonUrl ??= "/products";
        settings.IsAnnouncementEnabled = true;
        settings.AnnouncementText = "🎁 ۱۰٪ تخفیف اولین خرید با کد WELCOME10 | عضویت رایگان در باشگاه مشتریان";
        settings.AnnouncementUrl = "/club";
        settings.AnnouncementColor = BrandNavy;
        settings.InvoiceNote ??= "کالای فروخته‌شده تا ۷ روز با حفظ شرایط اولیه قابل بازگشت است. از خرید شما سپاسگزاریم.";

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Adds the personal home blocks once (also to existing installations). A block the
    /// admin deleted is soft deleted, so it is not added again.
    /// </summary>
    private static async Task SeedPersonalHomeSectionsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.HomeSections.IgnoreQueryFilters()
                .AnyAsync(s => s.Type == HomeSectionType.RecommendedForYou || s.Type == HomeSectionType.RecentlyViewed, cancellationToken))
            return;

        var bestSellers = await context.HomeSections
            .Where(s => s.Type == HomeSectionType.BestSellers)
            .Select(s => (int?)s.DisplayOrder)
            .FirstOrDefaultAsync(cancellationToken);
        var maxOrder = await context.HomeSections.Select(s => (int?)s.DisplayOrder).MaxAsync(cancellationToken) ?? 0;

        context.HomeSections.AddRange(
            new HomeSection
            {
                Type = HomeSectionType.RecommendedForYou,
                Title = "پیشنهاد ویژه برای شما",
                Subtitle = "بر اساس خریدها، علاقه‌مندی‌ها و بازدیدهای شما",
                ItemCount = 10,
                DisplayOrder = bestSellers ?? maxOrder + 1,
            },
            new HomeSection
            {
                Type = HomeSectionType.RecentlyViewed,
                Title = "بازدیدهای اخیر شما",
                ItemCount = 10,
                DisplayOrder = maxOrder + 1,
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedMenusAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Menus.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var header = new Menu { Key = "header-main", Title = "منوی اصلی", Location = MenuLocation.HeaderMain, DisplayOrder = 1 };
        header.Items.AddRange(
        [
            Item("فروشگاه", "/products", 1, icon: "Grid", type: MenuItemType.CategoriesMegaMenu),
            Item("شگفت انگیز ها", "/products?discounted=true", 2, icon: "Percent"),
            Item("پرفروش ترین ها", "/products?sort=BestSelling", 3, icon: "Fire"),
            Item("باشگاه مشتریان", "/club", 4, icon: "Crown", badge: "جدید"),
            Item("وبلاگ", "/blog", 5),
            Item("سوالات متداول", "/faq", 6),
            Item("ارتباط با ما", "/contact-us", 7),
        ]);

        var shortcuts = new Menu { Key = "header-shortcuts", Title = "میانبرهای فروشگاه", Location = MenuLocation.HeaderShortcuts, DisplayOrder = 1 };
        shortcuts.Items.AddRange(
        [
            Item("همه محصولات", "/products", 1, icon: "Storefront"),
            Item("شگفت انگیز ها", "/products?discounted=true", 2, icon: "Percent"),
            Item("پرفروش ترین ها", "/products?sort=BestSelling", 3, icon: "Fire"),
            Item("جدیدترین ها", "/products?sort=Newest", 4, icon: "Tag"),
            Item("مقایسه محصولات", "/compare", 5, icon: "Grid"),
        ]);

        var quick = new Menu { Key = "footer-quick-links", Title = "دسترسی سریع", Location = MenuLocation.Footer, DisplayOrder = 1 };
        quick.Items.AddRange(
        [
            Item("صفحه اصلی", "/", 1),
            Item("همه محصولات", "/products", 2),
            Item("وبلاگ", "/blog", 3),
            Item("سوالات متداول", "/faq", 4),
            Item("تماس با ما", "/contact-us", 5),
        ]);

        // Filled automatically with the content pages marked "show in footer".
        var guide = new Menu { Key = "footer-guide", Title = "راهنما و قوانین", Location = MenuLocation.Footer, Source = MenuSource.ContentPages, DisplayOrder = 2 };

        var customer = new Menu { Key = "footer-customer", Title = "خدمات مشتریان", Location = MenuLocation.Footer, DisplayOrder = 3 };
        customer.Items.AddRange(
        [
            Item("پیگیری سفارش", "/track-order", 1),
            Item("باشگاه مشتریان", "/club", 2),
            Item("حساب کاربری", "/profile", 3),
            Item("کیف پول", "/profile/wallet", 4),
            Item("اعلان ها", "/profile/notifications", 5),
        ]);

        context.Menus.AddRange(header, shortcuts, quick, guide, customer);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedFilterAttributesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ProductFilterAttributes.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        // Global filters; a box only appears when the listed products have that specification.
        context.ProductFilterAttributes.AddRange(
            new ProductFilterAttribute { Title = "حافظه رم", FeatureKey = "حافظه رم", Slug = "ram", DisplayOrder = 1 },
            new ProductFilterAttribute { Title = "حافظه داخلی", FeatureKey = "حافظه داخلی", Slug = "storage", DisplayOrder = 2 },
            new ProductFilterAttribute { Title = "پردازنده", FeatureKey = "پردازنده", Slug = "cpu", DisplayOrder = 3 },
            new ProductFilterAttribute { Title = "اندازه صفحه نمایش", FeatureKey = "اندازه صفحه", Slug = "screen", DisplayOrder = 4 },
            new ProductFilterAttribute { Title = "حذف نویز", FeatureKey = "حذف نویز", Slug = "anc", DisplayOrder = 5 },
            new ProductFilterAttribute { Title = "مقاومت در برابر آب", FeatureKey = "مقاومت در برابر آب", Slug = "water", DisplayOrder = 6 },
            new ProductFilterAttribute { Title = "نوع اتصال", FeatureKey = "نوع اتصال", Slug = "connection", DisplayOrder = 7 });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCouponsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Coupons.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var now = DateTimeOffset.UtcNow;

        context.Coupons.AddRange(
            new Coupon
            {
                Code = "WELCOME10",
                Title = "۱۰٪ تخفیف اولین خرید",
                Description = "ویژه اولین سفارش؛ حداقل خرید ۱ میلیون تومان و حداکثر ۵۰۰ هزار تومان تخفیف.",
                Type = CouponType.Percentage,
                Value = 10,
                MaxDiscountAmount = 500_000,
                MinOrderAmount = 1_000_000,
                FirstOrderOnly = true,
                UsageLimitPerUser = 1,
                IsPublic = true,
            },
            new Coupon
            {
                Code = "FRATAK8",
                Title = "۸٪ تخفیف خریدهای ۲ تا ۵۰ میلیون تومان",
                Description = "برای سبدهای بین ۲ تا ۵۰ میلیون تومان؛ سقف تخفیف ۱ میلیون تومان؛ هر نفر ۳ بار.",
                Type = CouponType.Percentage,
                Value = 8,
                MaxDiscountAmount = 1_000_000,
                MinOrderAmount = 2_000_000,
                MaxOrderAmount = 50_000_000,
                UsageLimitPerUser = 3,
                IsPublic = true,
            },
            new Coupon
            {
                Code = "FREESHIP",
                Title = "ارسال رایگان",
                Description = "هزینه ارسال سفارش‌های بالای ۱.۵ میلیون تومان رایگان می‌شود.",
                Type = CouponType.FreeShipping,
                Value = 0,
                MinOrderAmount = 1_500_000,
                UsageLimitPerUser = null,
                IsPublic = true,
            },
            new Coupon
            {
                Code = "SAVE300",
                Title = "۳۰۰ هزار تومان تخفیف",
                Description = "برای خرید بالای ۳ میلیون تومان از کالاهای بدون تخفیف؛ محدود به ۱۰۰ نفر اول.",
                Type = CouponType.FixedAmount,
                Value = 300_000,
                MinOrderAmount = 3_000_000,
                ExcludeDiscountedProducts = true,
                UsageLimit = 100,
                UsageLimitPerUser = 1,
                IsPublic = true,
            },
            new Coupon
            {
                Code = "EXPIRED50",
                Title = "کد منقضی‌شده (نمونه)",
                Description = "نمونه کد منقضی برای تست پیام‌های خطا.",
                Type = CouponType.Percentage,
                Value = 50,
                StartsAt = now.AddDays(-30),
                EndsAt = now.AddDays(-1),
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedLoyaltyAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.LoyaltyRules.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            context.LoyaltyRules.AddRange(
                new LoyaltyRule { Action = LoyaltyAction.Registration, Title = "عضویت در باشگاه", Description = "هدیه ثبت‌نام در سایت", Points = 100, MaxTimesPerUser = 1, DisplayOrder = 1 },
                new LoyaltyRule { Action = LoyaltyAction.CompleteProfile, Title = "تکمیل اطلاعات حساب", Description = "ثبت نام، نام خانوادگی و ایمیل", Points = 50, MaxTimesPerUser = 1, DisplayOrder = 2 },
                new LoyaltyRule { Action = LoyaltyAction.FirstOrder, Title = "اولین خرید", Description = FirstOrderRuleText, Points = 200, MaxTimesPerUser = 1, DisplayOrder = 3 },
                new LoyaltyRule { Action = LoyaltyAction.Purchase, Title = "هر خرید", Description = PurchaseRuleText, PointsPerStep = 1, AmountStep = 10_000, MaxPointsPerEvent = 2000, DisplayOrder = 4 },
                new LoyaltyRule { Action = LoyaltyAction.ProductReview, Title = "ثبت دیدگاه", Description = "برای هر دیدگاه تأییدشده (حداکثر ۲۰ بار)", Points = 20, MaxTimesPerUser = 20, DisplayOrder = 5 },
                new LoyaltyRule { Action = LoyaltyAction.Referral, Title = "معرفی دوستان", Description = "وقتی دوست شما با کد معرف ثبت‌نام کند و اولین خریدش را انجام دهد", Points = 300, DisplayOrder = 6 },
                new LoyaltyRule { Action = LoyaltyAction.ReferralSignup, Title = "ثبت‌نام با کد معرف", Description = "هدیه کاربر جدیدی که با کد معرف ثبت‌نام می‌کند", Points = 100, MaxTimesPerUser = 1, DisplayOrder = 7 },
                new LoyaltyRule { Action = LoyaltyAction.Birthday, Title = "هدیه تولد", Description = "سالی یک بار در روز تولد (تاریخ تولد را در اطلاعات حساب ثبت کنید)", Points = 150, DisplayOrder = 8 });
        }

        LoyaltyTier? silver = null;
        if (!await context.LoyaltyTiers.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            silver = new LoyaltyTier { Name = "نقره‌ای", MinLifetimePoints = 2000, PointsMultiplier = 1.2m, Color = "#64748b", Benefits = "۲۰٪ امتیاز بیشتر در هر خرید", DisplayOrder = 2 };
            context.LoyaltyTiers.AddRange(
                new LoyaltyTier { Name = "برنزی", MinLifetimePoints = 0, PointsMultiplier = 1m, Color = "#b45309", Benefits = "دریافت امتیاز از هر خرید و فعالیت", DisplayOrder = 1 },
                silver,
                new LoyaltyTier { Name = "طلایی", MinLifetimePoints = 6000, PointsMultiplier = 1.5m, Color = "#d97706", Benefits = "۵۰٪ امتیاز بیشتر و جوایز ویژه", DisplayOrder = 3 },
                new LoyaltyTier { Name = "الماسی", MinLifetimePoints = 15000, PointsMultiplier = 2m, Color = "#0069FD", Benefits = "امتیاز دوبرابر در همه خریدها", DisplayOrder = 4 });
        }

        if (!await context.LoyaltyRewards.IgnoreQueryFilters().AnyAsync(cancellationToken))
        {
            context.LoyaltyRewards.AddRange(
                new LoyaltyReward { Title = "۲۵ هزار تومان اعتبار کیف پول", Type = LoyaltyRewardType.WalletCredit, PointsCost = 500, Value = 25_000, DisplayOrder = 1 },
                new LoyaltyReward { Title = "کد ارسال رایگان", Description = "قابل استفاده تا ۳۰ روز", Type = LoyaltyRewardType.FreeShippingCoupon, PointsCost = 800, Value = 0, CouponValidDays = 30, DisplayOrder = 2 },
                new LoyaltyReward { Title = "کد ۱۰٪ تخفیف", Description = "حداقل خرید ۱ میلیون تومان، سقف ۳۰۰ هزار تومان، اعتبار ۳۰ روز", Type = LoyaltyRewardType.PercentageCoupon, PointsCost = 1000, Value = 10, MaxDiscountAmount = 300_000, MinOrderAmount = 1_000_000, CouponValidDays = 30, DisplayOrder = 3 },
                new LoyaltyReward { Title = "۲۰۰ هزار تومان اعتبار کیف پول", Description = "ویژه سطح نقره‌ای و بالاتر", Type = LoyaltyRewardType.WalletCredit, PointsCost = 3000, Value = 200_000, MinTier = silver, DisplayOrder = 4 });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedNotificationTemplatesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.NotificationTemplates.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.NotificationTemplates.AddRange(
            Template(NotificationEvent.Welcome, "به {SiteName} خوش آمدید", "{Name} عزیز، عضویت شما با موفقیت انجام شد. از این پس سفارش‌ها، کیف پول و امتیازهای باشگاه مشتریان را از حساب کاربری خود مدیریت کنید."),
            Template(NotificationEvent.OrderPlaced, "سفارش {OrderNumber} ثبت شد", "سفارش شما به مبلغ {Amount} تومان ثبت شد.", "مشاهده سفارش", sms: "سفارش {OrderNumber} در {SiteName} ثبت شد."),
            Template(NotificationEvent.OrderPaid, "پرداخت سفارش {OrderNumber} موفق بود", "مبلغ {Amount} تومان با موفقیت پرداخت شد و سفارش شما برای آماده‌سازی ارسال شد.", "مشاهده سفارش", sms: "پرداخت سفارش {OrderNumber} موفق بود."),
            Template(NotificationEvent.OrderConfirmed, "سفارش {OrderNumber} تأیید شد", "سفارش شما تأیید شد و به‌زودی آماده‌سازی می‌شود.", "مشاهده سفارش"),
            Template(NotificationEvent.OrderProcessing, "سفارش {OrderNumber} در حال آماده‌سازی است", "سفارش شما در انبار در حال آماده‌سازی است.", "مشاهده سفارش"),
            Template(NotificationEvent.OrderShipped, "سفارش {OrderNumber} ارسال شد", "سفارش شما تحویل مأمور ارسال شد. کد رهگیری: {TrackingCode}", "پیگیری سفارش", sms: "سفارش {OrderNumber} ارسال شد. کد رهگیری: {TrackingCode}"),
            Template(NotificationEvent.OrderDelivered, "سفارش {OrderNumber} تحویل شد", "امیدواریم از خرید خود راضی باشید. با ثبت دیدگاه برای کالاها امتیاز باشگاه مشتریان بگیرید.", "مشاهده سفارش"),
            Template(NotificationEvent.OrderCancelled, "سفارش {OrderNumber} لغو شد", "سفارش شما لغو شد. {RefundText}", "مشاهده سفارش"),
            Template(NotificationEvent.OrderReturned, "مرجوعی سفارش {OrderNumber} ثبت شد", "مرجوعی سفارش شما ثبت شد. {RefundText}", "مشاهده سفارش"),
            Template(NotificationEvent.PaymentFailed, "پرداخت سفارش {OrderNumber} ناموفق بود", "پرداخت انجام نشد. تا پایان مهلت پرداخت می‌توانید دوباره از صفحه سفارش اقدام کنید.", "پرداخت مجدد"),
            Template(NotificationEvent.WalletTopUp, "کیف پول شارژ شد", "مبلغ {Amount} تومان به کیف پول شما اضافه شد. موجودی فعلی: {Balance} تومان.", "مشاهده کیف پول"),
            Template(NotificationEvent.WalletAdjusted, "تغییر موجودی کیف پول", "موجودی کیف پول شما به {Balance} تومان تغییر کرد. {Description}", "مشاهده کیف پول"),
            Template(NotificationEvent.WithdrawalRequested, "درخواست برداشت ثبت شد", "درخواست برداشت {Amount} تومان ثبت شد و پس از بررسی به حساب شما واریز می‌شود.", "مشاهده کیف پول"),
            Template(NotificationEvent.WithdrawalApproved, "درخواست برداشت تأیید شد", "درخواست برداشت {Amount} تومان تأیید شد و به‌زودی واریز می‌شود.", "مشاهده کیف پول"),
            Template(NotificationEvent.WithdrawalPaid, "مبلغ برداشت واریز شد", "مبلغ {Amount} تومان به حساب بانکی شما واریز شد.", "مشاهده کیف پول"),
            Template(NotificationEvent.WithdrawalRejected, "درخواست برداشت رد شد", "درخواست برداشت {Amount} تومان رد شد و مبلغ به کیف پول شما برگشت.", "مشاهده کیف پول"),
            Template(NotificationEvent.PointsEarned, "{Points} امتیاز گرفتید", "بابت «{Description}» {Points} امتیاز باشگاه مشتریان گرفتید. امتیاز فعلی: {Balance}.", "باشگاه مشتریان"),
            Template(NotificationEvent.TierUpgraded, "تبریک! سطح شما {Tier} شد", "با امتیازهای جمع‌شده به سطح {Tier} باشگاه مشتریان رسیدید و از مزایای بیشتری بهره‌مند می‌شوید.", "باشگاه مشتریان"),
            Template(NotificationEvent.RewardRedeemed, "جایزه «{Reward}» دریافت شد", "{Points} امتیاز برای «{Reward}» کسر شد. {Description}", "باشگاه مشتریان"),
            Template(NotificationEvent.PasswordChanged, "رمز عبور تغییر کرد", "رمز عبور حساب شما تغییر کرد و همه دستگاه‌های دیگر از حساب خارج شدند. اگر این کار توسط شما انجام نشده، فوراً با پشتیبانی تماس بگیرید.", "امنیت حساب"),
            Template(NotificationEvent.TwoFactorEnabled, "ورود دومرحله‌ای فعال شد", "از این پس ورود با رمز عبور به کد پیامکی نیز نیاز دارد.", "امنیت حساب"),
            Template(NotificationEvent.TwoFactorDisabled, "ورود دومرحله‌ای غیرفعال شد", "ورود دومرحله‌ای حساب شما خاموش شد. اگر این کار توسط شما انجام نشده، فوراً رمز عبور را تغییر دهید.", "امنیت حساب"));

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedBroadcastsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == null, cancellationToken))
            return;

        var now = DateTimeOffset.UtcNow;

        context.Notifications.AddRange(
            new Notification
            {
                Audience = NotificationAudience.AllCustomers,
                Category = NotificationCategory.Promotion,
                Title = "🎉 جشنواره تخفیف فراتک",
                Body = "با کد FRATAK8 روی خریدهای ۲ تا ۵۰ میلیون تومان ۸٪ تخفیف (تا سقف ۱ میلیون تومان) بگیرید.",
                LinkUrl = "/products?discounted=true",
                LinkText = "مشاهده شگفت‌انگیزها",
                IsPinned = true,
                PublishAt = now,
            },
            new Notification
            {
                Audience = NotificationAudience.AllCustomers,
                Category = NotificationCategory.Loyalty,
                Title = "باشگاه مشتریان راه‌اندازی شد",
                Body = "با هر خرید، ثبت دیدگاه و معرفی دوستان امتیاز بگیرید و امتیازها را به اعتبار کیف پول یا کد تخفیف تبدیل کنید.",
                LinkUrl = "/club",
                LinkText = "ورود به باشگاه",
                PublishAt = now,
            },
            new Notification
            {
                Audience = NotificationAudience.CustomersWithoutOrders,
                Category = NotificationCategory.Promotion,
                Title = "هدیه اولین خرید شما",
                Body = "با کد WELCOME10 روی اولین سفارش خود ۱۰٪ تخفیف بگیرید.",
                LinkUrl = "/products",
                LinkText = "شروع خرید",
                PublishAt = now,
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static MenuItem Item(string title, string url, int order, string? icon = null, string? badge = null,
        MenuItemType type = MenuItemType.Link) => new()
    {
        Title = title,
        Url = url,
        DisplayOrder = order,
        Icon = icon,
        BadgeText = badge,
        Type = type,
    };

    private static NotificationTemplate Template(NotificationEvent @event, string title, string body, string? linkText = null, string? sms = null) => new()
    {
        Event = @event,
        Title = title,
        Body = body,
        LinkText = linkText,
        SendSms = sms is not null,
        SmsText = sms,
    };
}
