namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>نوع ورودی هر تنظیم. Image (مرحلهٔ دهم) = آدرس تصویر + دکمهٔ «انتخاب / آپلود از کتابخانهٔ رسانه» + پیش‌نمایش.</summary>
public enum SKind { Text, Area, Bool, Int, Money, Decimal, Url, Color, Select, Ltr, Image }

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
    private static SField Img(string name, string label, string? help = null) => new(name, label, SKind.Image, help);
    private static SField D(string name, string label, string? help = null) => new(name, label, SKind.Decimal, help);

    /// <summary>تبدیل فهرست (عدد، عنوان) به گزینه‌های فهرست کشویی. مثال: [(0,"ساده")] ← [("0","ساده")].</summary>
    private static (string Value, string Title)[] Opt((int Value, string Title)[] items) =>
        items.Select(i => (i.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture), i.Title)).ToArray();

    public static readonly SGroup[] Groups =
    [
        new("general", "عمومی", "Storefront", "نام، معرفی و ظاهر فروشگاه",
        [
            T("SiteName", "نام فروشگاه"), T("Tagline", "شعار"), A("SiteDescription", "معرفی کوتاه فروشگاه", "در فوتر و توضیحات پیش‌فرض متا استفاده می‌شود."),
            new("ThemeColor", "رنگ اصلی (PWA و نوار مرورگر)", SKind.Color, "خالی = سرمه‌ای برند #0B1927"),
            B("IsDarkModeEnabled", "حالت تاریک برای مشتریان"),
            new("DefaultTheme", "پوسته پیش‌فرض", SKind.Select) { Options = [("0", "روشن"), ("1", "تاریک"), ("2", "مطابق دستگاه")] },
        ]),
        // مرحلهٔ دوازدهم: «برند و لوگو» به صفحهٔ جدای «استودیوی لوگو» رفت (/admin/settings/brand) – تنظیم هر محل با پیش‌نمایش زنده.
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
            // دور ۲۱: ظاهر کامل نوار اطلاعیه (سبک، رنگ‌ها، اندازه و وزن قلم، جلوهٔ جلب توجه، پیام‌های چرخشی، دکمهٔ بستن).
            new("AnnouncementStyle", "سبک نوار", SKind.Select) { ShowWhen = "IsAnnouncementEnabled", Options = Opt(Fratak_Shop.Web.Infrastructure.AnnouncementLook.Styles) },
            new("AnnouncementColor2", "رنگ دوم (گرادیان)", SKind.Color, "خالی = همان رنگ نوار کمی روشن‌تر") { ShowWhen = "IsAnnouncementEnabled" },
            new("AnnouncementTextColor", "رنگ متن", SKind.Color, "خالی = سفید") { ShowWhen = "IsAnnouncementEnabled" },
            new("AnnouncementFontSize", "اندازهٔ متن", SKind.Select) { ShowWhen = "IsAnnouncementEnabled", Options = Opt(Fratak_Shop.Web.Infrastructure.AnnouncementLook.Sizes) },
            new("AnnouncementFontWeight", "وزن قلم", SKind.Select) { ShowWhen = "IsAnnouncementEnabled", Options = Opt(Fratak_Shop.Web.Infrastructure.AnnouncementLook.Weights) },
            new("AnnouncementEffect", "جلوهٔ جلب توجه", SKind.Select, "برای کاربرانی که «کاهش حرکت» را در دستگاه روشن کرده‌اند خودکار خاموش می‌شود.") { ShowWhen = "IsAnnouncementEnabled", Options = Opt(Fratak_Shop.Web.Infrastructure.AnnouncementLook.Effects) },
            new("AnnouncementMessages", "پیام‌های چرخشی (هر خط یک پیام)", SKind.Area, "هر ۵ ثانیه پیام بعدی نمایش داده می‌شود؛ حداکثر ۶ پیام.") { ShowWhen = "IsAnnouncementEnabled", Wide = true },
            new("IsAnnouncementCloseable", "مشتری بتواند نوار را ببندد", SKind.Bool) { ShowWhen = "IsAnnouncementEnabled" },
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
            // مرحلهٔ دهم: سیاست قیمت فروش وقتی قیمت خرید عوض شد (Core/Services/Suppliers/SupplierRepricing.cs).
            new("SupplierPriceAutoMode", "اعمال تغییر قیمت خرید", SKind.Select, "«افزایش‌ها خودکار» یعنی اگر کسی یادش رفت تأیید بزند، کالا با قیمت قدیمیِ زیان‌ده فروخته نمی‌شود.")
            {
                Options = [("0", "همیشه با تأیید کارمند"), ("1", "افزایش‌ها خودکار، کاهش‌ها با تأیید (پیشنهادی)"), ("2", "همهٔ تغییرها خودکار")],
            },
            new("SupplierRepriceMode", "روش محاسبهٔ قیمت فروش تازه", SKind.Select, "مثال: خرید ۲۰۰ → ۴۰۰ و فروش فعلی ۳۰۰ هزار. حفظ درصد سود → ۶۰۰؛ حفظ مبلغ سود → ۵۰۰؛ بیشترینِ دو حالت → ۶۰۰.")
            {
                Options = [("1", "حفظ درصد سود (فروش × خرید تازه ÷ خرید قبلی)"), ("2", "حفظ مبلغ سود (فروش + اختلاف خرید)"), ("5", "بیشترینِ «حفظ درصد» و «حفظ مبلغ» (محتاطانه)"),
                           ("3", "درصد ثابت روی قیمت خرید"), ("4", "مبلغ ثابت روی قیمت خرید"), ("0", "فقط قیمت خرید به‌روز شود (قیمت فروش دستی)")],
            },
            I("SupplierRepriceMarkupPercent", "درصد سود ثابت (روش «درصد ثابت»)", "مثال: ۲۵ ← خرید ۴۰۰ هزار → فروش ۵۰۰ هزار."),
            M("SupplierRepriceMarkupAmount", "مبلغ سود ثابت – تومان (روش «مبلغ ثابت»)", "مثال: ۵۰٬۰۰۰ ← خرید ۴۰۰ هزار → فروش ۴۵۰ هزار."),
            I("SupplierRepriceMinMarginPercent", "حداقل سود همیشگی (درصد روی خرید)", "در همهٔ روش‌ها قیمت فروش (و قیمت با تخفیف) از خرید × (۱ + این درصد) کمتر نمی‌شود. ۰ = فقط زیان نکند."),
            M("SupplierRepriceRounding", "گرد کردن به بالا – تومان", "مثال: ۱۰۰۰ ← ۵۱۸٬۷۵۰ → ۵۱۹٬۰۰۰. ۰ = بدون گرد کردن."),
            B("SupplierRepriceOnDecrease", "با ارزان شدن خرید، قیمت فروش هم پایین بیاید", "خاموش = قیمت فروش ثابت می‌ماند و سود بیشتر می‌شود."),
            I("SupplierRepriceMaxJumpPercent", "سقف افزایش خودکار (درصد)", "افزایش بیشتر از این (شاید خطای خواندن سایت) خودکار اعمال نمی‌شود؛ کالا تا تأیید شما موقتاً ناموجود می‌شود. ۰ = بدون سقف."),
        ]),
        // مرحلهٔ دهم: دکمهٔ «پیشنهاد قیمت فروش» در فرم محصول (Core/Services/Pricing/PriceAdvisor.cs).
        new("pricing", "پیشنهاد قیمت فروش", "Calculator", "هزینه‌هایی که روی هر کالا حساب می‌شوند (چه روی محصول ثبت شده باشند چه نه)، سود هدف و جست‌وجوی قیمت رقبا برای دکمهٔ «پیشنهاد قیمت» در فرم محصول.",
        [
            M("PricingPackagingCost", "بسته‌بندی هر عدد – تومان"),
            M("PricingShippingCost", "سهم ارسال/کرایه که فروشگاه می‌پردازد – تومان", "مثلاً ارسال رایگان یا کرایهٔ رسیدن کالا از تأمین‌کننده."),
            M("PricingOtherCostAmount", "هزینه‌های ثابت دیگر هر عدد – تومان", "سهم اجاره، حقوق، لوازم و …"),
            D("PricingGatewayFeePercent", "کارمزد درگاه پرداخت (درصد از فروش)", "مثال: ۱"),
            D("PricingOtherCostPercent", "هزینه‌های درصدی دیگر (درصد از فروش)", "تبلیغات، مرجوعی، تخفیف‌ها. مثال: ۳"),
            D("PricingTaxPercent", "مالیات بر ارزش افزوده (درصد)", "۰ = مشمول نیست."),
            I("PricingTargetMarginPercent", "سود خالص هدف (درصد روی هزینهٔ تمام‌شده)", "مثال: ۳۰"),
            I("PricingMinMarginPercent", "کمترین سود خالص قابل‌قبول (درصد)", "پیشنهاد هیچ‌وقت پایین‌تر از این نمی‌رود، حتی اگر رقبا ارزان‌تر باشند."),
            D("PricingUndercutPercent", "چند درصد ارزان‌تر از رقبا", "مثال: ۲ ← ارزان‌ترین رقیب معتبر ۲۷۰ هزار → حدود ۲۶۴ هزار."),
            M("PricingRounding", "گرد کردن قیمت پیشنهادی – تومان", "مثال: ۱۰۰۰"),
            B("IsCompetitorPriceLookupEnabled", "جست‌وجوی قیمت رقبا", "ترب (ده‌ها فروشگاه) و دیجی‌کالا؛ رایگان و بدون کلید. اگر سرور به این سایت‌ها دسترسی نداشته باشد، پیشنهاد فقط بر اساس هزینه‌ها و سود هدف است."),
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
            new("LogFileRetentionDays", "نگهداری فایل‌های گزارش روزانه (روز)", SKind.Int, "۱ تا ۱۸۰. هشدارها و خطاها هر روز در App_Data/logs ذخیره و در «گزارش‌های سرور» نمایش داده می‌شوند. پیشنهاد: ۱۴"),
        ]),
        // ── مرحلهٔ هفتم – فاز ۹: پشتیبان‌گیری خودکار ──
        new("backup", "پشتیبان‌گیری", "Database", "پشتیبان خودکار پایگاه داده (و در صورت نیاز فایل‌های آپلود) با زمان‌بندی، بررسی سلامت و پاک کردن نسخه‌های قدیمی. فهرست و اجرای دستی: «فنی ← پشتیبان‌گیری».",
        [
            B("IsBackupEnabled", "پشتیبان‌گیری خودکار پایگاه داده", "با دستور BACKUP خود SQL Server (COPY_ONLY تا زنجیرهٔ پشتیبان‌های دیگر شما به هم نخورد)."),
            new("BackupRunsPerDay", "تعداد پشتیبان در شبانه‌روز", SKind.Select, "با فاصلهٔ مساوی از ساعت شروع.") { ShowWhen = "IsBackupEnabled", Options = [("1", "۱ بار"), ("2", "۲ بار (هر ۱۲ ساعت)"), ("3", "۳ بار (هر ۸ ساعت)"), ("4", "۴ بار (هر ۶ ساعت)")] },
            new("BackupStartHour", "ساعت شروع (به وقت ایران)", SKind.Int, "۰ تا ۲۳؛ ساعت کم‌بازدید را انتخاب کنید. پیشنهاد: ۳ بامداد") { ShowWhen = "IsBackupEnabled" },
            new("BackupKeepCount", "تعداد نسخه‌های نگه‌داشته‌شده", SKind.Int, "۱ تا ۶۰؛ نسخه‌های قدیمی‌تر خودکار پاک می‌شوند (پشتیبان‌های دستی هم شمرده می‌شوند). پیشنهاد: ۷") { ShowWhen = "IsBackupEnabled" },
            new("BackupFolder", "پوشهٔ پشتیبان روی سرور پایگاه داده", SKind.Ltr, "خالی = پوشهٔ پیش‌فرض SQL Server. مسیر کامل، مثال: D:\\Backups یا /var/opt/mssql/backup. حساب سرویس SQL Server باید اجازهٔ نوشتن داشته باشد.") { ShowWhen = "IsBackupEnabled", Wide = true },
            new("IsBackupVerifyEnabled", "بررسی سلامت فایل پشتیبان", SKind.Bool, "بعد از هر پشتیبان RESTORE VERIFYONLY اجرا می‌شود تا از سالم بودن فایل مطمئن شوید.") { ShowWhen = "IsBackupEnabled" },
            new("IsUploadsBackupEnabled", "پشتیبان فایل‌های آپلود (تصاویر)", SKind.Bool, "در هر نوبت یک فایل zip از پوشهٔ uploads در App_Data/backups ساخته می‌شود. برای سایت‌های پرتصویر فضای زیادی می‌گیرد.") { ShowWhen = "IsBackupEnabled" },
        ]),
        // ── مرحلهٔ هفتم – فاز ۹: تعمیرات، فایروال و سلامت ──
        new("ops", "تعمیرات و امنیت سرور", "Wrench", "حالت تعمیرات سایت، مسدودسازی خودکار IPهای مهاجم و توکن بررسی سلامت برای سرویس‌های مانیتورینگ. قانون‌های IP و ربات: «فنی ← فایروال».",
        [
            B("IsMaintenanceMode", "حالت تعمیرات", "بازدیدکنندگان صفحهٔ «در حال به‌روزرسانی» (کد ۵۰۳) می‌بینند؛ کارکنان واردشده و IPهای مجاز سایت را عادی می‌بینند. پنل مدیریت همیشه باز است."),
            new("MaintenanceMessage", "پیام صفحهٔ تعمیرات", SKind.Area, "خالی = پیام پیش‌فرض. مثال: در حال افزودن محصولات جدید هستیم؛ تا ساعت ۱۸ برمی‌گردیم.") { ShowWhen = "IsMaintenanceMode", Wide = true },
            new("MaintenanceRetryMinutes", "زمان تخمینی بازگشت (دقیقه)", SKind.Int, "۵ تا ۱۴۴۰؛ به موتورهای جست‌وجو گفته می‌شود چه زمانی دوباره سر بزنند (Retry-After) تا رتبهٔ سایت آسیب نبیند.") { ShowWhen = "IsMaintenanceMode" },
            new("MaintenanceAllowedIps", "IPهای مجاز در حالت تعمیرات", SKind.Area, "هر IP یا محدوده (CIDR) در یک خط یا با کاما. مثال: 5.160.10.20 یا 5.160.10.0/24") { ShowWhen = "IsMaintenanceMode", Wide = true },
            B("IsIpAutoBlockEnabled", "مسدودسازی خودکار IP مهاجم", "IPهایی که در یک ساعت بیش از آستانه ورود ناموفق داشته باشند، موقتاً مسدود می‌شوند. IPهای شبکهٔ داخلی و فهرست «مجاز» هرگز مسدود نمی‌شوند."),
            new("IpAutoBlockThreshold", "آستانهٔ ورود ناموفق در یک ساعت", SKind.Int, "۵ تا ۱۰۰۰. پیشنهاد: ۲۰ (کاربر عادی هرگز به این عدد نمی‌رسد).") { ShowWhen = "IsIpAutoBlockEnabled" },
            new("IpAutoBlockHours", "مدت مسدودی (ساعت)", SKind.Int, "۱ تا ۷۲۰. پیشنهاد: ۲۴") { ShowWhen = "IsIpAutoBlockEnabled" },
            new("HealthCheckToken", "توکن بررسی سلامت (/healthz)", SKind.Ltr, "خالی = /healthz فقط وضعیت کلی (ok/fail) می‌دهد. با توکن (۱۶ تا ۱۰۰ نویسه) جزئیات هم برمی‌گردد: /healthz?token=... مناسب UptimeRobot و مانند آن.") { Wide = true },
        ]),
        // ── مرحلهٔ هفتم – فاز ۱۰: امنیت کارکنان (هر گزینه جداگانه روشن/خاموش می‌شود) ──
        new("team-security", "امنیت کارکنان", "ShieldCheck", "برنامهٔ احراز هویت برای ورود کارکنان، تأیید دوبارهٔ هویت پیش از کارهای حساس، تأیید دونفره برای کارهای مالی بزرگ و پوشاندن اطلاعات شخصی مشتری. هر کارمند از «امنیت حساب من» برنامه را راه‌اندازی می‌کند.",
        [
            B("IsStaffTotpEnabled", "برنامهٔ احراز هویت (Google Authenticator و …)", "کارکنان می‌توانند ورودشان را با کد ۶ رقمی گوشی (بدون پیامک و رایگان) محکم کنند. اگر فعال باشد، به‌جای کد پیامکی دومرحله‌ای پرسیده می‌شود."),
            new("IsStaffTotpRequired", "اجباری برای همهٔ کارکنان", SKind.Bool, "کارمندی که فعال نکرده، با ورود به پنل اول به صفحهٔ راه‌اندازی می‌رود و تا فعال نکند بخش دیگری باز نمی‌شود. قبل از روشن کردن، به کارکنان خبر دهید.") { ShowWhen = "IsStaffTotpEnabled" },
            B("IsReauthEnabled", "تأیید دوبارهٔ هویت برای کارهای حساس", "پیش از تغییر کیف پول، پرداخت برداشت، تغییر نقش‌ها، تأیید دونفره، بازگردانی پشتیبان و … رمز یا کد دوباره پرسیده می‌شود (اگر کسی پشت سیستم باز کارمند بنشیند، نمی‌تواند این کارها را انجام دهد)."),
            new("ReauthMinutes", "اعتبار هر تأیید (دقیقه)", SKind.Int, "۵ تا ۲۴۰؛ در این مدت دوباره پرسیده نمی‌شود. پیشنهاد: ۱۵") { ShowWhen = "IsReauthEnabled" },
            B("IsTwoPersonApprovalEnabled", "تأیید دونفره", "کارهای بزرگ‌تر از سقف‌های زیر ثبت می‌شوند و بعد از تأیید یک همکار دیگرِ دارای همان دسترسی خودکار انجام می‌شوند («تأییدهای دونفره»). اگر هیچ همکار دیگری آن دسترسی را نداشته باشد، ثبت درخواست ممکن نیست و راهنما نمایش داده می‌شود."),
            new("TwoPersonWalletThreshold", "سقف تغییر دستی کیف پول (تومان)", SKind.Money, "افزایش یا کاهش دستی از این مبلغ به بالا تأیید دوم می‌خواهد. پیشنهاد: ۱٬۰۰۰٬۰۰۰") { ShowWhen = "IsTwoPersonApprovalEnabled" },
            new("TwoPersonWithdrawalThreshold", "سقف پرداخت برداشت (تومان)", SKind.Money, "علامت «پرداخت شد» برای برداشت‌های از این مبلغ به بالا تأیید دوم می‌خواهد. پیشنهاد: ۵٬۰۰۰٬۰۰۰") { ShowWhen = "IsTwoPersonApprovalEnabled" },
            new("IsTwoPersonRoleChangeEnabled", "تأیید دوم برای تغییر نقش کارکنان", SKind.Bool, "تغییر نقش‌ها یا مالک کردن یک کارمند هم تأیید دوم لازم دارد (جلوی بالا بردن دسترسی خودسرانه را می‌گیرد).") { ShowWhen = "IsTwoPersonApprovalEnabled" },
            B("IsDataMaskingEnabled", "پوشاندن اطلاعات شخصی مشتری", "موبایل، ایمیل و نشانی مشتری برای کارکنانی که دسترسی «دیدن اطلاعات کامل مشتری» ندارند به شکل 0912•••4567 نمایش داده می‌شود؛ شبا و کارت فقط برای «تسویهٔ برداشت‌ها» کامل است."),
        ]),
    ];
}
