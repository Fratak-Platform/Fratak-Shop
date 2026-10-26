namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

public enum SKind { Text, Area, Bool, Int, Money, Decimal, Url, Color, Select, Ltr }

public sealed record SField(string Name, string Label, SKind Kind = SKind.Text, string? Help = null)
{
    public (string Value, string Title)[] Options { get; init; } = [];
    public string? ShowWhen { get; init; }
    public bool Wide { get; init; }
}

public sealed record SGroup(string Key, string Title, string Icon, string? Description, SField[] Fields);

/// <summary>
/// The store settings editable in the admin panel, grouped into tabs. Every field maps to a property of
/// <c>StoreSetting</c> / <c>StoreSettingCommand</c>; the service validates all values before saving.
/// </summary>
public static class SettingsRegistry
{
    private static SField B(string name, string label, string? help = null) => new(name, label, SKind.Bool, help);
    private static SField I(string name, string label, string? help = null) => new(name, label, SKind.Int, help);
    private static SField M(string name, string label, string? help = null) => new(name, label, SKind.Money, help);
    private static SField T(string name, string label, string? help = null) => new(name, label, SKind.Text, help);
    private static SField A(string name, string label, string? help = null) => new(name, label, SKind.Area, help) { Wide = true };
    private static SField U(string name, string label, string? help = null) => new(name, label, SKind.Url, help);

    public static readonly SGroup[] Groups =
    [
        new("general", "عمومی", "Storefront", "نام، معرفی و ظاهر فروشگاه",
        [
            T("SiteName", "نام فروشگاه"), T("Tagline", "شعار"), A("SiteDescription", "معرفی کوتاه فروشگاه", "در فوتر و توضیحات پیش‌فرض متا استفاده می‌شود."),
            U("LogoUrl", "آدرس لوگو", "خالی = لوگوی پیش‌فرض"), U("FaviconUrl", "آدرس فاوآیکن"),
            new("ThemeColor", "رنگ اصلی (PWA و نوار مرورگر)", SKind.Color),
            B("IsDarkModeEnabled", "حالت تاریک برای مشتریان"),
            new("DefaultTheme", "پوسته پیش‌فرض", SKind.Select) { Options = [("0", "روشن"), ("1", "تاریک"), ("2", "مطابق دستگاه")] },
        ]),
        new("contact", "تماس و شبکه‌ها", "Phone", "اطلاعات تماس نمایش‌داده‌شده در سایت",
        [
            new("SupportPhone", "تلفن پشتیبانی", SKind.Ltr), new("SupportEmail", "ایمیل پشتیبانی", SKind.Ltr), A("Address", "نشانی"),
            new("PostalCode", "کد پستی", SKind.Ltr), T("WorkingHours", "ساعات کاری"), U("MapEmbedUrl", "لینک نقشه (embed)", "فقط https"),
            U("InstagramUrl", "اینستاگرام"), U("TelegramUrl", "تلگرام"), U("WhatsAppUrl", "واتساپ"), U("LinkedinUrl", "لینکدین"),
            U("TwitterUrl", "ایکس (توییتر)"), U("AparatUrl", "آپارات"), U("YoutubeUrl", "یوتیوب"),
        ]),
        new("footer", "فوتر و اطلاعیه", "Megaphone", "متن‌های فوتر و نوار اطلاعیه بالای سایت",
        [
            B("IsAnnouncementEnabled", "نوار اطلاعیه بالای سایت"),
            new("AnnouncementText", "متن اطلاعیه", SKind.Text) { ShowWhen = "IsAnnouncementEnabled" },
            new("AnnouncementUrl", "لینک اطلاعیه", SKind.Url) { ShowWhen = "IsAnnouncementEnabled" },
            new("AnnouncementColor", "رنگ نوار", SKind.Color) { ShowWhen = "IsAnnouncementEnabled" },
            T("FooterTitle", "عنوان فوتر"), T("FooterSubtitle", "زیرعنوان فوتر"), A("FooterText", "متن فوتر"), T("CopyrightText", "متن کپی‌رایت"),
            T("FooterButtonText", "متن دکمه فوتر"), U("FooterButtonUrl", "لینک دکمه فوتر"),
        ]),
        new("orders", "سفارش و ارسال", "Package", "قوانین ثبت، پرداخت و آماده‌سازی سفارش",
        [
            M("MinimumOrderAmount", "حداقل مبلغ سفارش (تومان)", "۰ = بدون محدودیت"), M("FreeShippingThreshold", "ارسال رایگان از مبلغ (تومان)", "۰ = غیرفعال"),
            I("UnpaidOrderTimeoutMinutes", "مهلت پرداخت سفارش (دقیقه)", "پس از این مدت سفارش پرداخت‌نشده لغو و موجودی آزاد می‌شود."),
            I("DeliveryDaysAhead", "تعداد روزهای قابل انتخاب برای تحویل"), new("DeliveryOffDays", "روزهای بدون ارسال", SKind.Ltr, "شماره روزها با کاما: ۰=یکشنبه … ۵=جمعه، ۶=شنبه"),
            I("SameDayLeadMinutes", "حداقل فاصله تا بازه تحویل همان روز (دقیقه)"),
            B("AllowCustomerCancellation", "لغو سفارش توسط مشتری (قبل از آماده‌سازی)", "مبلغ پرداخت‌شده به کیف پول برمی‌گردد."),
            B("IsGuestOrderTrackingEnabled", "پیگیری سفارش بدون ورود"),
            B("RequirePurchaseInvoiceForProcessing", "الزام آپلود فاکتور خرید هنگام رفتن به «در حال آماده‌سازی»", "توصیه: همیشه فعال بماند."),
            I("LowStockThreshold", "آستانه هشدار کمبود موجودی"),
            I("PaymentLinkHours", "اعتبار لینک پرداخت سفارش‌های ثبت‌شده در پنل (ساعت)", "بعد از این مدت سفارش پرداخت‌نشده خودکار لغو و موجودی آزاد می‌شود."),
        ]),
        // زمان‌بندی سفارش (SLA): مهلت هر مرحله؛ محاسبه در Core/Services/Orders/OrderSla.cs.
        new("sla", "زمان‌بندی سفارش", "Timer", "مهلت هر مرحلهٔ رسیدگی به سفارش؛ سفارش‌های دیرکرد در پنل نشان‌دار می‌شوند و یک بار به کارکنان هشدار می‌رود.",
        [
            B("IsOrderSlaEnabled", "زمان‌بندی و هشدار دیرکرد سفارش‌ها"),
            new("SlaSupplierOrderHours", "مهلت ثبت خرید در تأمین‌کننده (ساعت)", SKind.Int, "از پرداخت (یا ثبت سفارش پرداخت در محل) تا «در حال آماده‌سازی». پیشنهاد: ۲۴") { ShowWhen = "IsOrderSlaEnabled" },
            new("SlaPreparationHours", "مهلت آماده‌سازی و تحویل به پست (ساعت)", SKind.Int, "از «در حال آماده‌سازی» تا ثبت کد رهگیری/ارسال. پیشنهاد: ۴۸") { ShowWhen = "IsOrderSlaEnabled" },
            new("SlaTrackingCodeHours", "مهلت ثبت کد رهگیری بعد از ارسال (ساعت)", SKind.Int, "وقتی وضعیت «ارسال‌شده» است ولی کد رهگیری ثبت نشده. پیشنهاد: ۱۲") { ShowWhen = "IsOrderSlaEnabled" },
            I("SlaDeliveryMinDays", "کمترین روز تحویل پس از ارسال", "اگر روش ارسال سفارش روزهای تحویل خودش را داشته باشد، همان ملاک است. مثال پست: ۳"),
            I("SlaDeliveryMaxDays", "بیشترین روز تحویل پس از ارسال", "بعد از این مدت «تأیید تحویل» دیرکرد حساب می‌شود. مثال پست: ۴ (کل فرایند معمولاً ۳ تا ۷ روز)"),
            new("SlaWarningPercent", "هشدار «نزدیک به پایان مهلت» از چند درصد مهلت", SKind.Int, "مثال: ۷۵ یعنی وقتی سه‌چهارم مهلت گذشت، نشان نارنجی نمایش داده می‌شود.") { ShowWhen = "IsOrderSlaEnabled" },
        ]),
        // بررسی خودکار لینک تأمین‌کننده؛ موتور در Core/Services/Suppliers، گزارش در /admin/products/supplier-sync.
        new("supplier", "بررسی تأمین‌کننده", "ArrowsClockwise", "خواندن خودکار موجودی و قیمت هر محصول از لینک منبع خرید و گزارش تغییرها برای تأیید.",
        [
            B("IsSupplierSyncEnabled", "بررسی خودکار موجودی و قیمت تأمین‌کننده"),
            new("SupplierSyncRunsPerDay", "تعداد بررسی در شبانه‌روز", SKind.Int, "۱ تا ۲۴؛ با فاصلهٔ مساوی از نیمه‌شب به وقت ایران. مثال: ۴ → ساعت ۰، ۶، ۱۲ و ۱۸.") { ShowWhen = "IsSupplierSyncEnabled" },
            new("SupplierAutoOutOfStock", "ناموجود کردن خودکار", SKind.Bool, "اگر کالا در منبع تمام شد، موجودی فروشگاه بلافاصله صفر می‌شود (و اگر موجودی ما از منبع بیشتر بود، کم می‌شود) تا سفارش بدون کالا ثبت نشود. برگشت موجودی همیشه با تأیید شماست.") { ShowWhen = "IsSupplierSyncEnabled" },
            new("SupplierStockMarginPercent", "حاشیهٔ اطمینان موجودی (درصد)", SKind.Int, "موجودی پیشنهادی ما این مقدار کمتر از منبع است. مثال: منبع ۵۰ و حاشیهٔ ۲۰ → پیشنهاد ۴۰.") { ShowWhen = "IsSupplierSyncEnabled" },
            new("SupplierStockMaxQuantity", "سقف موجودی پیشنهادی هر کالا", SKind.Int, "حتی اگر منبع موجودی زیادی داشته باشد، بیشتر از این پیشنهاد نمی‌شود.") { ShowWhen = "IsSupplierSyncEnabled" },
            new("SupplierRequestDelayMs", "فاصلهٔ درخواست‌ها به سایت منبع (میلی‌ثانیه)", SKind.Int, "برای اینکه سایت تأمین‌کننده ما را ربات مزاحم تشخیص ندهد. پیشنهاد: ۱۵۰۰") { ShowWhen = "IsSupplierSyncEnabled" },
        ]),
        // رهگیری خودکار مرسوله‌های پستی؛ موتور در Core/Services/Orders/ShipmentTrackingService.cs.
        new("tracking", "رهگیری مرسوله", "Truck", "خواندن خودکار وضعیت مرسوله‌های پستی از سامانهٔ رهگیری پست و تأیید خودکار تحویل.",
        [
            B("IsPostTrackingEnabled", "رهگیری خودکار مرسوله‌های پستی", "سامانهٔ رهگیری پست فقط از IP ایران پاسخ می‌دهد؛ سرور باید در ایران باشد."),
            new("PostTrackingRunsPerDay", "تعداد بررسی هر مرسوله در شبانه‌روز", SKind.Int, "۱ تا ۱۲؛ با فاصلهٔ مساوی از نیمه‌شب به وقت ایران. پیشنهاد: ۴ (ساعت ۰، ۶، ۱۲ و ۱۸).") { ShowWhen = "IsPostTrackingEnabled" },
            new("PostTrackingAutoDeliver", "تأیید خودکار تحویل", SKind.Bool, "با دیدن «تحویل به گیرنده» در سامانهٔ پست، سفارش خودکار «تحویل‌شده» می‌شود و پیام تحویل برای مشتری می‌رود.") { ShowWhen = "IsPostTrackingEnabled" },
            new("PostTrackingMaxDays", "حداکثر روزهای رهگیری", SKind.Int, "اگر تا این مدت بعد از ثبت کد تحویل نشد، بررسی متوقف و به کارکنان هشدار داده می‌شود. پیشنهاد: ۳۰") { ShowWhen = "IsPostTrackingEnabled" },
        ]),
        // آمار بازدید و قیف فروش؛ جمع‌آوری در Web/Infrastructure/Analytics و گزارش در /admin/reports.
        new("analytics", "آمار بازدید", "ChartLine", "ثبت بازدید، قیف خرید و منبع ورودی بازدیدکنندگان برای گزارش‌های «قیف فروش»، «کمپین‌ها» و «عملکرد محصولات».",
        [
            B("IsAnalyticsEnabled", "ثبت آمار بازدید", "بدون سرویس خارجی و بدون ذخیرهٔ IP؛ هر مرورگر با یک شناسهٔ تصادفی شمرده می‌شود و ربات‌ها شمرده نمی‌شوند."),
            new("AnalyticsRetentionDays", "مدت نگهداری جزئیات بازدید (روز)", SKind.Int, "۳۰ تا ۱۵۰۰؛ بعد از آن ردیف‌های هر بازدیدکننده پاک می‌شوند ولی آمار بازدید محصولات می‌ماند. پیشنهاد: ۴۰۰") { ShowWhen = "IsAnalyticsEnabled" },
        ]),
        new("payment", "پرداخت", "CreditCard", "درگاه پرداخت و پرداخت در محل",
        [
            B("IsOnlinePaymentEnabled", "پرداخت آنلاین (زرین‌پال)"),
            new("ZarinPalMerchantId", "مرچنت کد زرین‌پال", SKind.Ltr, "شناسه ۳۶ کاراکتری؛ در حالت آزمایشی خالی بماند.") { ShowWhen = "IsOnlinePaymentEnabled" },
            new("ZarinPalSandbox", "حالت آزمایشی (Sandbox)", SKind.Bool, "در محیط واقعی حتماً خاموش شود.") { ShowWhen = "IsOnlinePaymentEnabled" },
            B("IsCashOnDeliveryEnabled", "پرداخت در محل"),
            new("CashOnDeliveryMaxAmount", "سقف پرداخت در محل (تومان)", SKind.Money, "۰ = بدون سقف") { ShowWhen = "IsCashOnDeliveryEnabled" },
            new("CodMinDeliveredOrders", "حداقل خرید موفق قبلی برای پرداخت در محل", SKind.Int, "۰ = بدون شرط؛ ۱ یعنی پرداخت در محل بعد از اولین سفارش تحویل‌شده فعال می‌شود.") { ShowWhen = "IsCashOnDeliveryEnabled" },
            new("CodMaxReturnedOrders", "بستن پرداخت در محل پس از چند سفارش مرجوع‌شده", SKind.Int, "۰ = بدون محدودیت؛ ۲ یعنی با دومین سفارش پرداخت در محلِ تحویل‌نگرفته، این روش برای مشتری بسته می‌شود.") { ShowWhen = "IsCashOnDeliveryEnabled" },
            new("CodAllowedProvinces", "استان‌های مجاز پرداخت در محل", SKind.Area, "نام استان‌ها با کاما یا در خطوط جدا؛ خالی = همهٔ استان‌ها. مثال: تهران، البرز") { ShowWhen = "IsCashOnDeliveryEnabled", Wide = true },
        ]),
        new("wallet", "کیف پول", "Wallet", "شارژ، پرداخت و برداشت از کیف پول (اعتبار هدیه هرگز قابل برداشت نیست)",
        [
            B("IsWalletEnabled", "کیف پول"), B("IsWalletPaymentEnabled", "پرداخت سفارش با کیف پول"), B("IsWalletTopUpEnabled", "شارژ آنلاین"),
            B("IsWithdrawalEnabled", "درخواست برداشت وجه"),
            M("WalletMinTopUpAmount", "حداقل شارژ (تومان)"), M("WalletMaxTopUpAmount", "حداکثر شارژ (تومان)"),
            M("WalletMinWithdrawalAmount", "حداقل برداشت (تومان)"), M("WalletMaxWithdrawalAmount", "حداکثر برداشت (تومان)", "۰ = بدون سقف"),
            I("WalletMaxPendingWithdrawals", "حداکثر درخواست برداشت باز هر کاربر"),
        ]),
        new("security", "ورود و امنیت", "ShieldCheck", "روش‌های ورود و محافظت از حساب‌ها",
        [
            B("IsPasswordLoginEnabled", "ورود با رمز عبور"), B("IsOtpLoginEnabled", "ورود با کد پیامکی"), B("IsOtpRegistrationEnabled", "ثبت‌نام با کد پیامکی"),
            B("IsTwoFactorAvailable", "امکان فعال‌سازی ورود دومرحله‌ای"), B("IsCaptchaEnabled", "کپچای هوشمند پس از تلاش‌های ناموفق"),
            new("CaptchaAfterFailedAttempts", "نمایش کپچا پس از چند تلاش ناموفق", SKind.Int) { ShowWhen = "IsCaptchaEnabled" },
            B("IsNewDeviceAlertEnabled", "هشدار ورود از دستگاه جدید"),
        ]),
        new("invoice", "فاکتور", "Receipt", "اطلاعات فروشنده روی فاکتور مشتری",
        [
            T("OrganizationLegalName", "نام حقوقی فروشگاه"), new("SellerEconomicCode", "کد اقتصادی", SKind.Ltr), new("SellerNationalId", "شناسه ملی", SKind.Ltr),
            new("SellerRegistrationNumber", "شماره ثبت", SKind.Ltr), new("InvoiceNumberPrefix", "پیشوند شماره فاکتور", SKind.Ltr, "فقط حروف لاتین، مثلاً FR"),
            A("InvoiceNote", "یادداشت پایین فاکتور"),
        ]),
        // موتور سئوی هوشمند داخلی؛ موتور در Core/Services/Seo/Engine و صف بررسی در /admin/seo/engine.
        new("seo-engine", "موتور سئو", "MagicWand", "بررسی خودکار سئوی همهٔ صفحه‌ها، نوشتن پیشنهاد (عنوان، توضیحات، متن، پرسش‌وپاسخ) و صف تأیید برای کارشناس سئو. بدون هیچ سرویس پولی.",
        [
            B("IsSeoEngineEnabled", "اجرای خودکار موتور سئو", "هیچ تغییری بدون تأیید کارشناس روی سایت اعمال نمی‌شود؛ موتور فقط نمره می‌دهد و پیشنهاد می‌سازد."),
            new("SeoEngineRunsPerDay", "تعداد اجرا در شبانه‌روز", SKind.Int, "۱ تا ۶؛ با فاصلهٔ مساوی از ساعت ۲ بامداد به وقت ایران. پیشنهاد: ۳ (ساعت ۲، ۱۰ و ۱۸).") { ShowWhen = "IsSeoEngineEnabled" },
            new("SeoEngineBatchSize", "حداکثر پیشنهاد تازه در هر اجرا", SKind.Int, "۵ تا ۲۰۰. نمرهٔ همهٔ صفحه‌ها در هر اجرا به‌روز می‌شود؛ این عدد فقط تعداد صفحه‌هایی است که پیشنهاد تازه می‌گیرند. پیشنهاد: ۳۰") { ShowWhen = "IsSeoEngineEnabled" },
            B("SeoCompetitorResearch", "تحقیق کلمه و تحلیل رقبا از اینترنت", "پیشنهادهای جست‌وجوی گوگل/بینگ و صفحه‌های برتر نتایج را رایگان می‌خواند (هر عبارت حداکثر هفته‌ای یک بار). اگر سرور به این سایت‌ها دسترسی نداشته باشد، موتور فقط با داده‌های خود فروشگاه کار می‌کند."),
            B("SeoAllowSlugChanges", "پیشنهاد اصلاح نامک", "فقط برای صفحه‌های تازه یا کم‌بازدید، و همیشه با ریدایرکت ۳۰۱ خودکار از آدرس قبلی تا رتبهٔ صفحه از دست نرود."),
        ]),
        new("seo", "سئو", "Globe", "تنظیمات پیش‌فرض سئو کل سایت",
        [
            T("TitleSeparator", "جداکننده عنوان صفحه", "مثلاً | یا –"), A("DefaultMetaKeywords", "کلمات کلیدی پیش‌فرض"), U("DefaultOgImageUrl", "تصویر پیش‌فرض اشتراک‌گذاری (OG)"),
            new("GoogleSiteVerification", "کد تأیید Google Search Console", SKind.Ltr), new("BingSiteVerification", "کد تأیید Bing", SKind.Ltr),
            new("XSiteHandle", "شناسه ایکس (@handle)", SKind.Ltr), new("RobotsTxtExtra", "خطوط اضافه robots.txt", SKind.Area) { Wide = true },
        ]),
        new("loyalty", "باشگاه و معرفی", "Crown", "باشگاه مشتریان، امتیاز، کوپن و معرفی دوستان",
        [
            B("IsLoyaltyEnabled", "باشگاه مشتریان"), T("LoyaltyProgramName", "نام باشگاه"), I("LoyaltyPointsExpireDays", "اعتبار امتیازها (روز)", "خالی = نامحدود"),
            A("LoyaltyProgramDescription", "معرفی باشگاه"), B("IsReferralEnabled", "معرفی دوستان"), A("LoyaltyReferralDescription", "توضیح معرفی دوستان"),
            A("ReferralShareText", "متن اشتراک‌گذاری لینک معرفی"), I("ReferralCookieDays", "اعتبار لینک معرفی (روز)"), B("IsCouponEnabled", "کد تخفیف در سبد خرید"),
        ]),
        new("gift-cards", "کارت هدیه", "Gift", "فروش و استفاده از کارت هدیه",
        [
            B("IsGiftCardEnabled", "فروش کارت هدیه"), M("GiftCardMinAmount", "حداقل مبلغ (تومان)"), M("GiftCardMaxAmount", "حداکثر مبلغ (تومان)"),
            new("GiftCardPresetAmounts", "مبالغ پیشنهادی", SKind.Ltr, "با کاما، مثلاً 200000,500000"), I("GiftCardValidDays", "مدت اعتبار (روز)"),
            B("IsGiftCardCreditWithdrawable", "اعتبار کارت هدیه قابل برداشت باشد", "پیشنهاد: خاموش (اعتبار فقط برای خرید)."), A("GiftCardTerms", "قوانین کارت هدیه"),
        ]),
        new("marketing", "بازاریابی", "Percent", "سبد رهاشده، خبرم کن و پیشنهادها",
        [
            B("IsAbandonedCartEnabled", "پیگیری سبدهای رهاشده"), I("AbandonedCartAfterMinutes", "رهاشده پس از (دقیقه)"), M("AbandonedCartMinAmount", "حداقل مبلغ سبد (تومان)"),
            I("AbandonedCartExpireDays", "مدت پیگیری (روز)"), B("IsProductAlertEnabled", "خبرم کن (موجود شدن / کاهش قیمت)"), I("ProductAlertMaxPerUser", "حداکثر خبرم کن هر کاربر"),
            B("IsRecommendationEnabled", "پیشنهاد محصول به مشتری"),
        ]),
        new("support", "نظرات، پشتیبانی و مرجوعی", "Headset", "دیدگاه‌ها، تیکت، مرجوعی و نظرسنجی",
        [
            B("IsReviewOnlyForBuyers", "دیدگاه فقط برای خریداران"), B("IsReviewImageEnabled", "ارسال تصویر در دیدگاه"), I("ReviewMaxImages", "حداکثر تصاویر دیدگاه"),
            I("ReviewMaxImageSizeKb", "حداکثر حجم هر تصویر (کیلوبایت)"), B("IsCustomerAnswerEnabled", "پاسخ مشتریان به پرسش‌ها"), B("IsCommentVotingEnabled", "رأی مفید/نامفید"),
            B("IsTicketEnabled", "تیکت پشتیبانی"), I("TicketMaxOpenPerUser", "حداکثر تیکت باز هر کاربر"), I("TicketMaxAttachments", "حداکثر پیوست هر پیام"),
            I("TicketMaxAttachmentSizeKb", "حداکثر حجم پیوست (کیلوبایت)"), B("IsReturnEnabled", "درخواست مرجوعی"), I("ReturnWindowDays", "مهلت مرجوعی پس از تحویل (روز)"),
            I("ReturnMaxImages", "حداکثر تصاویر مرجوعی"), A("ReturnPolicyText", "متن قوانین مرجوعی"), B("IsOrderFeedbackEnabled", "نظرسنجی پس از تحویل"),
            I("OrderFeedbackWindowDays", "مهلت ثبت نظرسنجی (روز)"),
        ]),
        new("catalog", "جست‌وجو و فروشگاه", "MagnifyingGlass", "جست‌وجو، مقایسه و امکانات صفحه محصول",
        [
            B("IsInstantSearchEnabled", "جست‌وجوی لحظه‌ای"), B("IsSearchSynonymsEnabled", "مترادف‌های جست‌وجو"), B("IsDidYouMeanEnabled", "«منظورتان این بود؟»"),
            B("IsPopularSearchesEnabled", "جست‌وجوهای پرطرفدار"), I("PopularSearchesCount", "تعداد جست‌وجوهای پرطرفدار"), I("PopularSearchMinCount", "حداقل تکرار برای پرطرفدار شدن"),
            B("IsSmartCompareEnabled", "مقایسه هوشمند"), B("IsProductVideoEnabled", "ویدیوی محصول"), B("IsSaveForLaterEnabled", "ذخیره برای بعد"),
            B("IsPriceHistoryEnabled", "نمودار تاریخچه قیمت"), I("PriceHistoryDays", "بازه نمودار قیمت (روز)"),
        ]),
        new("map", "نقشه", "MapPin", "انتخاب موقعیت آدرس روی نقشه",
        [
            B("IsAddressMapEnabled", "انتخاب موقعیت روی نقشه"), new("MapTileUrl", "آدرس تایل نقشه", SKind.Ltr, "مثلاً https://tile.openstreetmap.org/{z}/{x}/{y}.png"),
            T("MapAttribution", "متن منبع نقشه"), new("MapDefaultLatitude", "عرض جغرافیایی پیش‌فرض", SKind.Decimal), new("MapDefaultLongitude", "طول جغرافیایی پیش‌فرض", SKind.Decimal),
            I("MapDefaultZoom", "بزرگ‌نمایی پیش‌فرض"),
        ]),
        new("pwa", "اپلیکیشن و اعلان", "DeviceMobile", "نصب وب‌اپ (PWA) و اعلان مرورگر",
        [
            B("IsPwaEnabled", "وب‌اپلیکیشن (PWA)"), T("PwaShortName", "نام کوتاه اپلیکیشن"), B("IsPwaInstallPromptEnabled", "پیشنهاد نصب"),
            I("PwaInstallPromptAfterPages", "پیشنهاد پس از چند صفحه"), I("PwaInstallPromptSnoozeDays", "فاصله تکرار پیشنهاد (روز)"),
            T("PwaInstallPromptTitle", "عنوان پیشنهاد نصب"), A("PwaInstallPromptText", "متن پیشنهاد نصب"),
            B("IsWebPushEnabled", "اعلان مرورگر (Web Push)", "کلیدهای VAPID به‌صورت خودکار ساخته می‌شوند."), new("VapidSubject", "ایمیل/آدرس فرستنده اعلان (VAPID subject)", SKind.Ltr),
        ]),
        new("performance", "عملکرد و نگهداری", "Lightning", "کش، بهینه‌سازی تصاویر و نگهداری لاگ‌ها",
        [
            B("IsPageCacheEnabled", "کش صفحات عمومی", "با هر تغییر داده، کش مرتبط خودکار پاک می‌شود."), I("PageCacheSeconds", "مدت کش صفحه (ثانیه)"),
            B("IsImageOptimizationEnabled", "بهینه‌سازی و تغییر اندازه تصاویر"), I("ImageQuality", "کیفیت تصاویر (۱ تا ۱۰۰)"),
            I("ErrorLogRetentionDays", "نگهداری لاگ خطا (روز)"), I("AuditLogRetentionDays", "نگهداری گزارش فعالیت (روز)"),
        ]),
    ];
}
