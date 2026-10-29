using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Settings;

/// <summary>
/// Store-wide settings (single row). Everything the storefront used to read from
/// appsettings.json or hard-coded text – store identity, contact details, payment
/// switches, wallet limits, delivery rules, SEO defaults – lives here so it can be
/// edited from the admin panel without touching code.
/// </summary>
public class StoreSetting : BaseEntity
{
    // ── Identity ─────────────────────────────────────────────────────────
    [Required]
    [MaxLength(200)]
    public string SiteName { get; set; } = null!;

    [MaxLength(300)]
    public string? Tagline { get; set; }

    /// <summary>Short description of the store (default meta description, footer, Organization schema).</summary>
    [MaxLength(500)]
    public string? SiteDescription { get; set; }

    /// <summary>Logo URL (absolute or site-relative). Empty = bundled logo.</summary>
    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(500)]
    public string? FaviconUrl { get; set; }

    // ── Contact ──────────────────────────────────────────────────────────
    [MaxLength(50)]
    public string? SupportPhone { get; set; }

    [MaxLength(255)]
    public string? SupportEmail { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }

    [MaxLength(200)]
    public string? WorkingHours { get; set; }

    /// <summary>Map iframe URL of the contact page (Google Maps / Neshan / OSM embed link, https only).</summary>
    [MaxLength(1000)]
    public string? MapEmbedUrl { get; set; }

    // ── Social networks ──────────────────────────────────────────────────
    [MaxLength(500)]
    public string? InstagramUrl { get; set; }

    [MaxLength(500)]
    public string? TelegramUrl { get; set; }

    [MaxLength(500)]
    public string? WhatsAppUrl { get; set; }

    [MaxLength(500)]
    public string? LinkedinUrl { get; set; }

    [MaxLength(500)]
    public string? TwitterUrl { get; set; }

    [MaxLength(500)]
    public string? AparatUrl { get; set; }

    [MaxLength(500)]
    public string? YoutubeUrl { get; set; }

    // ── Footer texts ─────────────────────────────────────────────────────
    [MaxLength(300)]
    public string? FooterTitle { get; set; }

    [MaxLength(300)]
    public string? FooterSubtitle { get; set; }

    [MaxLength(1000)]
    public string? FooterText { get; set; }

    [MaxLength(300)]
    public string? CopyrightText { get; set; }

    // ── Orders & delivery ────────────────────────────────────────────────
    /// <summary>Orders with items total equal or above this value ship for free (0 = disabled).</summary>
    public decimal FreeShippingThreshold { get; set; }

    /// <summary>Minimum items total of an order (0 = no minimum).</summary>
    public decimal MinimumOrderAmount { get; set; }

    /// <summary>Number of selectable delivery days shown at checkout.</summary>
    public int DeliveryDaysAhead { get; set; } = 6;

    /// <summary>Comma separated <see cref="DayOfWeek"/> numbers without delivery (5 = Friday).</summary>
    [MaxLength(20)]
    public string? DeliveryOffDays { get; set; } = "5";

    /// <summary>Same-day time slots can be chosen until this many minutes before the slot starts.</summary>
    public int SameDayLeadMinutes { get; set; } = 120;

    /// <summary>Unpaid online orders are cancelled (and their stock released) after this many minutes.</summary>
    public int UnpaidOrderTimeoutMinutes { get; set; } = 60;

    /// <summary>Customers may cancel their own order while it is not being prepared yet.</summary>
    public bool AllowCustomerCancellation { get; set; } = true;

    // ── Payment ──────────────────────────────────────────────────────────
    public bool IsOnlinePaymentEnabled { get; set; } = true;

    public bool IsCashOnDeliveryEnabled { get; set; } = true;

    /// <summary>Maximum payable amount allowed for cash on delivery (0 = unlimited).</summary>
    public decimal CashOnDeliveryMaxAmount { get; set; }

    [MaxLength(36)]
    public string? ZarinPalMerchantId { get; set; }

    public bool ZarinPalSandbox { get; set; } = true;

    // ── Wallet ───────────────────────────────────────────────────────────
    public bool IsWalletEnabled { get; set; } = true;

    public bool IsWalletPaymentEnabled { get; set; } = true;

    public bool IsWalletTopUpEnabled { get; set; } = true;

    public bool IsWithdrawalEnabled { get; set; } = true;

    public decimal WalletMinTopUpAmount { get; set; } = 10_000;

    public decimal WalletMaxTopUpAmount { get; set; } = 100_000_000;

    public decimal WalletMinWithdrawalAmount { get; set; } = 50_000;

    /// <summary>Maximum amount of one withdrawal request (0 = unlimited).</summary>
    public decimal WalletMaxWithdrawalAmount { get; set; }

    /// <summary>Open (pending) withdrawal requests a customer may have at the same time.</summary>
    public int WalletMaxPendingWithdrawals { get; set; } = 1;

    // ── SEO ──────────────────────────────────────────────────────────────
    [MaxLength(10)]
    public string TitleSeparator { get; set; } = "|";

    [MaxLength(500)]
    public string? DefaultMetaKeywords { get; set; }

    [MaxLength(500)]
    public string? DefaultOgImageUrl { get; set; }

    [MaxLength(200)]
    public string? GoogleSiteVerification { get; set; }

    [MaxLength(200)]
    public string? BingSiteVerification { get; set; }

    /// <summary>Extra lines appended to robots.txt (e.g. Disallow rules for special crawlers).</summary>
    [MaxLength(2000)]
    public string? RobotsTxtExtra { get; set; }

    /// <summary>X (Twitter) account of the shop, e.g. @fratak (twitter:site).</summary>
    [MaxLength(50)]
    public string? XSiteHandle { get; set; }

    /// <summary>Registered/legal name used in the Organization structured data.</summary>
    [MaxLength(200)]
    public string? OrganizationLegalName { get; set; }

    // ── Sign-in ──────────────────────────────────────────────────────────
    /// <summary>Sign-in with mobile number + password.</summary>
    public bool IsPasswordLoginEnabled { get; set; } = true;

    /// <summary>Sign-in with a one-time SMS code.</summary>
    public bool IsOtpLoginEnabled { get; set; } = true;

    /// <summary>A verified code for an unknown number creates the account (SMS sign-up).</summary>
    public bool IsOtpRegistrationEnabled { get; set; } = true;

    /// <summary>Customers may turn on two-step sign-in (password + SMS code).</summary>
    public bool IsTwoFactorAvailable { get; set; } = true;

    // ── Marketing ────────────────────────────────────────────────────────
    public bool IsCouponEnabled { get; set; } = true;

    /// <summary>Order tracking with order number + mobile number, without signing in.</summary>
    public bool IsGuestOrderTrackingEnabled { get; set; } = true;

    /// <summary>Personal recommendations / "bought together" / recently viewed blocks.</summary>
    public bool IsRecommendationEnabled { get; set; } = true;

    // ── Customer club ────────────────────────────────────────────────────
    public bool IsLoyaltyEnabled { get; set; } = true;

    [MaxLength(100)]
    public string? LoyaltyProgramName { get; set; }

    /// <summary>Earned points expire after this many days (null = never).</summary>
    public int? LoyaltyPointsExpireDays { get; set; }

    /// <summary>Invite friends with a personal code (rules: Referral / ReferralSignup).</summary>
    public bool IsReferralEnabled { get; set; } = true;

    /// <summary>Intro text of the club page.</summary>
    [MaxLength(1000)]
    public string? LoyaltyProgramDescription { get; set; }

    /// <summary>Text of the "invite friends" box.</summary>
    [MaxLength(500)]
    public string? LoyaltyReferralDescription { get; set; }

    // ── Abandoned carts ──────────────────────────────────────────────────
    public bool IsAbandonedCartEnabled { get; set; } = true;

    /// <summary>A signed-in customer's cart counts as abandoned after this many idle minutes.</summary>
    public int AbandonedCartAfterMinutes { get; set; } = 60;

    /// <summary>Carts below this items total are ignored (0 = all).</summary>
    public decimal AbandonedCartMinAmount { get; set; }

    /// <summary>Open abandoned carts are no longer followed after this many days.</summary>
    public int AbandonedCartExpireDays { get; set; } = 7;

    // ── Reviews ──────────────────────────────────────────────────────────
    public bool IsReviewImageEnabled { get; set; } = true;

    public int ReviewMaxImages { get; set; } = 3;

    public int ReviewMaxImageSizeKb { get; set; } = 2048;

    /// <summary>Only customers who received the product can write a review.</summary>
    public bool IsReviewOnlyForBuyers { get; set; }

    // ── Search ───────────────────────────────────────────────────────────
    /// <summary>Instant suggestions (products, categories, brands) while typing.</summary>
    public bool IsInstantSearchEnabled { get; set; } = true;

    // ── Announcement bar (top of every page) ────────────────────────────
    public bool IsAnnouncementEnabled { get; set; }

    [MaxLength(300)]
    public string? AnnouncementText { get; set; }

    [MaxLength(500)]
    public string? AnnouncementUrl { get; set; }

    /// <summary>Background colour (hex, e.g. #0B1927 – brand navy).</summary>
    [MaxLength(20)]
    public string? AnnouncementColor { get; set; }

    // ── Footer call to action ────────────────────────────────────────────
    [MaxLength(100)]
    public string? FooterButtonText { get; set; }

    [MaxLength(500)]
    public string? FooterButtonUrl { get; set; }

    // ── Invoice (seller details) ─────────────────────────────────────────
    [MaxLength(20)]
    public string? SellerEconomicCode { get; set; }

    [MaxLength(20)]
    public string? SellerNationalId { get; set; }

    [MaxLength(30)]
    public string? SellerRegistrationNumber { get; set; }

    [MaxLength(1000)]
    public string? InvoiceNote { get; set; }

    /// <summary>Prefix of serial invoice numbers (INV → INV-1405-000001).</summary>
    [MaxLength(10)]
    public string? InvoiceNumberPrefix { get; set; } = "INV";

    // ── Progressive web app ──────────────────────────────────────────────
    public bool IsPwaEnabled { get; set; } = true;

    [MaxLength(30)]
    public string? PwaShortName { get; set; }

    /// <summary>Browser / app theme colour (hex).</summary>
    [MaxLength(20)]
    public string? ThemeColor { get; set; }

    /// <summary>Suggest installing the app (custom banner) after the visitor has browsed a little.</summary>
    public bool IsPwaInstallPromptEnabled { get; set; } = true;

    /// <summary>Number of page views before the install suggestion appears.</summary>
    public int PwaInstallPromptAfterPages { get; set; } = 3;

    /// <summary>Days the suggestion stays hidden after the visitor closes it.</summary>
    public int PwaInstallPromptSnoozeDays { get; set; } = 14;

    [MaxLength(100)]
    public string? PwaInstallPromptTitle { get; set; }

    [MaxLength(300)]
    public string? PwaInstallPromptText { get; set; }

    // ═══ Round 5 ═══════════════════════════════════════════════════════

    // ── Product alerts (notify me) ──

    /// <summary>"Notify me" for back-in-stock and price drops. Channels follow the BackInStock / PriceDrop notification templates (in-site, push, SMS).</summary>
    public bool IsProductAlertEnabled { get; set; } = true;

    /// <summary>Active alerts a customer can have at the same time.</summary>
    public int ProductAlertMaxPerUser { get; set; } = 50;

    // ── Gift cards ──

    /// <summary>Customers can buy gift cards and redeem codes into their wallet.</summary>
    public bool IsGiftCardEnabled { get; set; } = true;

    public decimal GiftCardMinAmount { get; set; } = 100000m;

    public decimal GiftCardMaxAmount { get; set; } = 10000000m;

    /// <summary>Comma-separated amounts offered as quick choices.</summary>
    [MaxLength(200)]
    public string? GiftCardPresetAmounts { get; set; } = "200000,500000,1000000,2000000";

    /// <summary>Validity after purchase (0 = never expires).</summary>
    public int GiftCardValidDays { get; set; } = 365;

    /// <summary>Redeemed amount is withdrawable money. Default false: purchase-only credit, so gift cards can't be used to move money out.</summary>
    public bool IsGiftCardCreditWithdrawable { get; set; }

    /// <summary>Terms shown on the gift card page.</summary>
    [MaxLength(2000)]
    public string? GiftCardTerms { get; set; }

    // ── Referral sharing ──

    /// <summary>Text sent with the invite link ({Link}, {Code}, {SiteName}).</summary>
    [MaxLength(500)]
    public string? ReferralShareText { get; set; }

    /// <summary>Days an invite link is remembered on the visitor's browser.</summary>
    public int ReferralCookieDays { get; set; } = 30;

    // ── Smart captcha ──

    /// <summary>Ask for a captcha after repeated failures (sign-in, SMS code, gift card, contact form).</summary>
    public bool IsCaptchaEnabled { get; set; } = true;

    /// <summary>Failures (per IP and action) before the captcha is required.</summary>
    public int CaptchaAfterFailedAttempts { get; set; } = 3;

    // ── Security alerts ──

    /// <summary>Notify the customer when the account is used on a new device / browser.</summary>
    public bool IsNewDeviceAlertEnabled { get; set; } = true;

    // ── Cart ──

    /// <summary>"Next purchase" list in the cart.</summary>
    public bool IsSaveForLaterEnabled { get; set; } = true;

    // ── Price history ──

    /// <summary>Price history chart on the product page.</summary>
    public bool IsPriceHistoryEnabled { get; set; } = true;

    /// <summary>Days shown in the chart.</summary>
    public int PriceHistoryDays { get; set; } = 90;

    // ── Address map ──

    /// <summary>Customers can pick the address location on a map.</summary>
    public bool IsAddressMapEnabled { get; set; } = true;

    /// <summary>Map tile URL template ({z}/{x}/{y}); e.g. OpenStreetMap or an Iranian tile provider.</summary>
    [MaxLength(500)]
    public string? MapTileUrl { get; set; } = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    [MaxLength(200)]
    public string? MapAttribution { get; set; } = "© OpenStreetMap";

    /// <summary>Map centre when the customer has no location yet.</summary>
    public decimal MapDefaultLatitude { get; set; } = 35.6892m;

    public decimal MapDefaultLongitude { get; set; } = 51.3890m;

    public int MapDefaultZoom { get; set; } = 12;

    // ── Returns ──

    /// <summary>Online return requests for delivered orders.</summary>
    public bool IsReturnEnabled { get; set; } = true;

    /// <summary>Days after delivery a return can be requested.</summary>
    public int ReturnWindowDays { get; set; } = 7;

    public int ReturnMaxImages { get; set; } = 3;

    /// <summary>Short return conditions shown on the request form.</summary>
    [MaxLength(2000)]
    public string? ReturnPolicyText { get; set; }

    // ── Order feedback ──

    /// <summary>Customers rate the shopping experience of delivered orders.</summary>
    public bool IsOrderFeedbackEnabled { get; set; } = true;

    /// <summary>Days after delivery the order can be rated.</summary>
    public int OrderFeedbackWindowDays { get; set; } = 30;

    // ── Appearance ──

    /// <summary>Customers can switch to the dark theme.</summary>
    public bool IsDarkModeEnabled { get; set; } = true;

    /// <summary>0 = light, 1 = dark, 2 = follow the device setting.</summary>
    public int DefaultTheme { get; set; }

    // ── Smart search ──

    /// <summary>Expand queries with the synonym groups (SearchSynonyms).</summary>
    public bool IsSearchSynonymsEnabled { get; set; } = true;

    /// <summary>Show popular searches in the search box.</summary>
    public bool IsPopularSearchesEnabled { get; set; } = true;

    public int PopularSearchesCount { get; set; } = 8;

    /// <summary>Searches a query needs before it can be listed as popular.</summary>
    public int PopularSearchMinCount { get; set; } = 3;

    /// <summary>Suggest a correction ("did you mean …") when a search has no result.</summary>
    public bool IsDidYouMeanEnabled { get; set; } = true;

    // ── Questions & answers ──

    /// <summary>Customers can answer product questions (shown after approval, buyers get a badge).</summary>
    public bool IsCustomerAnswerEnabled { get; set; } = true;

    /// <summary>"Helpful" votes on reviews and answers.</summary>
    public bool IsCommentVotingEnabled { get; set; } = true;

    // ── Compare ──

    /// <summary>Suggest similar products on the compare page and highlight differences.</summary>
    public bool IsSmartCompareEnabled { get; set; } = true;

    // ── Product video ──

    public bool IsProductVideoEnabled { get; set; } = true;

    // ── Support tickets ──

    public bool IsTicketEnabled { get; set; } = true;

    /// <summary>Open tickets a customer can have at the same time.</summary>
    public int TicketMaxOpenPerUser { get; set; } = 5;

    public int TicketMaxAttachments { get; set; } = 3;

    public int TicketMaxAttachmentSizeKb { get; set; } = 2048;

    // ── Browser push notifications ──

    /// <summary>Browser push notifications (Web Push / VAPID; keys are generated automatically).</summary>
    public bool IsWebPushEnabled { get; set; } = true;

    /// <summary>Contact of the push sender (mailto: or https: URL). Defaults to the support e-mail / site URL.</summary>
    [MaxLength(200)]
    public string? VapidSubject { get; set; }

    /// <summary>Generated automatically (base64url P-256 public key).</summary>
    [MaxLength(200)]
    public string? VapidPublicKey { get; set; }

    // ── Image optimization ──

    /// <summary>Serve resized WebP versions of uploaded images (generated once, cached on disk).</summary>
    public bool IsImageOptimizationEnabled { get; set; } = true;

    /// <summary>WebP quality (40–95).</summary>
    public int ImageQuality { get; set; } = 80;

    // ── Page cache ──

    /// <summary>Cache public pages for guests; cleared automatically when the related data changes.</summary>
    public bool IsPageCacheEnabled { get; set; } = true;

    public int PageCacheSeconds { get; set; } = 300;

    // ── Admin panel / operations ─────────────────────────────────────

    /// <summary>An order can only move to "processing" after its supplier purchase invoice is uploaded.</summary>
    public bool RequirePurchaseInvoiceForProcessing { get; set; } = true;

    /// <summary>Products with this stock or less are reported as "low stock" in the panel.</summary>
    public int LowStockThreshold { get; set; } = 3;

    /// <summary>Error log entries older than this are removed.</summary>
    public int ErrorLogRetentionDays { get; set; } = 60;

    /// <summary>Audit / security log entries older than this are removed.</summary>
    public int AuditLogRetentionDays { get; set; } = 365;

    // ── زمان‌بندی سفارش (SLA) ─────────────────────────────────────────
    // هر مرحلهٔ سفارش یک مهلت دارد؛ اگر از مهلت بگذرد، سفارش در پنل «دیرکرد» نشان داده می‌شود و
    // یک بار به کارکنان هشدار می‌رود. محاسبه در Core/Services/Orders/OrderSla.cs انجام می‌شود.

    /// <summary>هشدار و نشان‌های زمان‌بندی سفارش فعال باشد.</summary>
    public bool IsOrderSlaEnabled { get; set; } = true;

    /// <summary>مهلت ثبت خرید در تأمین‌کننده (از پرداخت تا «در حال آماده‌سازی»)، به ساعت. پیش‌فرض ۲۴.</summary>
    public int SlaSupplierOrderHours { get; set; } = 24;

    /// <summary>مهلت آماده‌سازی و تحویل به پست (از «در حال آماده‌سازی» تا «ارسال‌شده»)، به ساعت. پیش‌فرض ۴۸.</summary>
    public int SlaPreparationHours { get; set; } = 48;

    /// <summary>مهلت ثبت کد رهگیری بعد از ارسال (وقتی وضعیت «ارسال‌شده» است ولی کد ندارد)، به ساعت. پیش‌فرض ۱۲.</summary>
    public int SlaTrackingCodeHours { get; set; } = 12;

    /// <summary>کمترین روز تحویل پس از ارسال (اگر روش ارسال مقدار خودش را نداشت). مثال پست: ۳.</summary>
    public int SlaDeliveryMinDays { get; set; } = 3;

    /// <summary>بیشترین روز تحویل پس از ارسال؛ بعد از آن «تأیید تحویل» دیرکرد حساب می‌شود. مثال پست: ۴.</summary>
    public int SlaDeliveryMaxDays { get; set; } = 4;

    /// <summary>وقتی این درصد از مهلت گذشت، نشان «نزدیک به پایان مهلت» نمایش داده می‌شود. پیش‌فرض ۷۵.</summary>
    public int SlaWarningPercent { get; set; } = 75;

    // ── سفارش ثبت‌شده توسط کارمند ───────────────────────────────────

    /// <summary>اعتبار لینک پرداخت سفارشی که کارمند ثبت می‌کند (ساعت). پس از آن سفارش خودکار لغو می‌شود.</summary>
    public int PaymentLinkHours { get; set; } = 48;

    // ── محدودیت‌های پرداخت در محل ────────────────────────────────────

    /// <summary>حداقل تعداد سفارش تحویل‌شدهٔ قبلی برای استفاده از پرداخت در محل (۰ = بدون شرط).</summary>
    public int CodMinDeliveredOrders { get; set; }

    /// <summary>
    /// اگر مشتری این تعداد سفارش پرداخت در محل مرجوع‌شده (تحویل‌نگرفته) داشته باشد، دیگر پرداخت در محل ندارد.
    /// ۰ = بدون محدودیت. مثال: ۲ یعنی با دومین مرجوعی، پرداخت در محل برایش بسته می‌شود.
    /// </summary>
    public int CodMaxReturnedOrders { get; set; }

    /// <summary>استان‌های مجاز پرداخت در محل، جداشده با کاما (خالی = همه). مثال: «تهران، البرز».</summary>
    [MaxLength(1000)]
    public string? CodAllowedProvinces { get; set; }

    // ── بررسی خودکار لینک تأمین‌کننده (مرحلهٔ هفتم – فاز ۵) ─────────────
    // چند بار در روز موجودی و قیمت هر محصول در سایت تأمین‌کننده خوانده و با فروشگاه مقایسه می‌شود.
    // پیاده‌سازی: Core/Services/Suppliers (موتور) و Web/Infrastructure/SupplierSyncWorker.cs (زمان‌بندی).

    /// <summary>بررسی خودکار موجودی و قیمت تأمین‌کننده فعال باشد.</summary>
    public bool IsSupplierSyncEnabled { get; set; } = true;

    /// <summary>تعداد اجرا در شبانه‌روز (۱ تا ۲۴)، با فاصلهٔ مساوی از نیمه‌شب به وقت ایران. مثال: ۴ → ساعت ۰، ۶، ۱۲ و ۱۸.</summary>
    public int SupplierSyncRunsPerDay { get; set; } = 4;

    /// <summary>اگر کالا در منبع ناموجود شد، موجودی فروشگاه خودکار صفر شود (و اگر موجودی ما از منبع بیشتر بود، کم شود).</summary>
    public bool SupplierAutoOutOfStock { get; set; } = true;

    /// <summary>
    /// موجودی پیشنهادی ما چند درصد کمتر از موجودی منبع باشد تا هرگز بیش از منبع نفروشیم.
    /// مثال: منبع ۵۰ عدد و حاشیهٔ ۲۰٪ → پیشنهاد ۴۰ (با رعایت سقف).
    /// </summary>
    public int SupplierStockMarginPercent { get; set; } = 20;

    /// <summary>سقف موجودی پیشنهادی برای هر کالا (حتی اگر منبع موجودی زیادی داشته باشد). مثال: ۱۰.</summary>
    public int SupplierStockMaxQuantity { get; set; } = 10;

    /// <summary>فاصلهٔ بین دو درخواست به یک سایت تأمین‌کننده (میلی‌ثانیه) تا فشار نیاورد و مسدود نشویم.</summary>
    public int SupplierRequestDelayMs { get; set; } = 1500;

    // ── رهگیری خودکار مرسوله‌های پستی (مرحلهٔ هفتم – فاز ۶) ─────────────
    // موتور: Core/Services/Orders/ShipmentTrackingService.cs؛ زمان‌بندی: Web/Infrastructure/Shipping.

    /// <summary>وضعیت مرسوله‌های پستی چند بار در روز از سامانهٔ رهگیری پست خوانده شود.</summary>
    public bool IsPostTrackingEnabled { get; set; } = true;

    /// <summary>تعداد بررسی هر مرسوله در شبانه‌روز (۱ تا ۱۲)، با فاصلهٔ مساوی از نیمه‌شب. مثال: ۴ → ساعت ۰، ۶، ۱۲ و ۱۸.</summary>
    public int PostTrackingRunsPerDay { get; set; } = 4;

    /// <summary>با دیدن «تحویل به گیرنده» در سامانهٔ پست، سفارش خودکار «تحویل‌شده» شود.</summary>
    public bool PostTrackingAutoDeliver { get; set; } = true;

    /// <summary>بعد از این تعداد روز از ثبت کد، اگر هنوز تحویل نشده بود بررسی متوقف و به کارکنان هشدار داده می‌شود. مثال: ۳۰.</summary>
    public int PostTrackingMaxDays { get; set; } = 30;

    // ── آمار بازدید، قیف فروش و منبع ورودی (مرحلهٔ هفتم – فاز ۷) ─────────
    // جمع‌آوری: Web/Infrastructure/Analytics؛ گزارش‌ها: Core/Services/Reports/AnalyticsReportService.cs.

    /// <summary>ثبت بازدید، مراحل قیف خرید و منبع ورودی (UTM/ارجاع‌دهنده) بازدیدکنندگان. بدون ذخیرهٔ IP.</summary>
    public bool IsAnalyticsEnabled { get; set; } = true;

    /// <summary>ردیف‌های روزانهٔ هر بازدیدکننده بعد از این تعداد روز پاک می‌شوند (شمارنده‌های جمعی می‌مانند). مثال: ۴۰۰.</summary>
    public int AnalyticsRetentionDays { get; set; } = 400;

    // ── موتور سئوی هوشمند داخلی (مرحلهٔ هفتم – فاز ۸) ──────────────────
    // موتور: Core/Services/Seo/Engine؛ زمان‌بندی: Web/Infrastructure/Seo؛ پنل: /admin/seo/engine.

    /// <summary>بررسی خودکار سئوی همهٔ صفحه‌ها و ساخت پیشنهاد برای تأیید کارشناس.</summary>
    public bool IsSeoEngineEnabled { get; set; } = true;

    /// <summary>تعداد اجرای خودکار در شبانه‌روز (۱ تا ۶)، با فاصلهٔ مساوی از ساعت ۲ بامداد. مثال: ۳ → ساعت ۲، ۱۰ و ۱۸.</summary>
    public int SeoEngineRunsPerDay { get; set; } = 3;

    /// <summary>حداکثر صفحه‌ای که در هر اجرا پیشنهاد تازه می‌گیرد (۵ تا ۲۰۰). نمره‌دهی همهٔ صفحه‌ها جدا انجام می‌شود.</summary>
    public int SeoEngineBatchSize { get; set; } = 30;

    /// <summary>تحقیق رایگان از اینترنت: پیشنهادهای جست‌وجو و تحلیل صفحه‌های برتر نتایج (رقبا).</summary>
    public bool SeoCompetitorResearch { get; set; } = true;

    /// <summary>پیشنهاد تغییر نامک فقط برای صفحه‌های تازه/کم‌بازدید و همیشه با ریدایرکت ۳۰۱ خودکار.</summary>
    public bool SeoAllowSlugChanges { get; set; } = true;

    // ── عملیات فنی (مرحلهٔ هفتم – فاز ۹) ──────────────────────────────
    // پشتیبان: Core/Services/Ops/DatabaseBackupService.cs؛ حالت تعمیر و دیوار آتش: Web/Infrastructure/Ops؛ پنل: /admin/system/*.

    /// <summary>پشتیبان‌گیری خودکار از پایگاه داده.</summary>
    public bool IsBackupEnabled { get; set; } = true;

    /// <summary>تعداد پشتیبان در شبانه‌روز (۱ تا ۴) با فاصلهٔ مساوی از BackupStartHour. مثال: ۲ و ساعت ۳ → ساعت ۳ و ۱۵.</summary>
    public int BackupRunsPerDay { get; set; } = 1;

    /// <summary>ساعت اولین پشتیبان به وقت ایران (۰ تا ۲۳). پیشنهاد: ۳ بامداد که سایت خلوت است.</summary>
    public int BackupStartHour { get; set; } = 3;

    /// <summary>چند نسخهٔ آخر نگه داشته شود (۱ تا ۶۰)؛ قدیمی‌ترها پاک می‌شوند.</summary>
    public int BackupKeepCount { get; set; } = 7;

    /// <summary>
    /// پوشهٔ فایل‌های پشتیبان روی سرور SQL Server. خالی = پوشهٔ پیش‌فرض خود SQL Server.
    /// مثال لینوکس: «/var/opt/mssql/backups»؛ ویندوز: «D:\Backups\Fratak».
    /// </summary>
    [MaxLength(400)]
    public string? BackupFolder { get; set; }

    /// <summary>بعد از هر پشتیبان، صحت فایل با RESTORE VERIFYONLY بررسی شود.</summary>
    public bool IsBackupVerifyEnabled { get; set; } = true;

    /// <summary>از پوشهٔ آپلودها (تصویرها) هم فایل zip ساخته شود (در App_Data/backups سرور سایت).</summary>
    public bool IsUploadsBackupEnabled { get; set; }

    /// <summary>حالت تعمیر: بازدیدکنندگان صفحهٔ «در حال به‌روزرسانی» با کد ۵۰۳ می‌بینند؛ کارکنان و پنل مدیریت عادی کار می‌کنند.</summary>
    public bool IsMaintenanceMode { get; set; }

    /// <summary>متن صفحهٔ تعمیر. مثال: «در حال به‌روزرسانی فروشگاه هستیم؛ تا ساعت ۱۴ برمی‌گردیم.»</summary>
    [MaxLength(1000)]
    public string? MaintenanceMessage { get; set; }

    /// <summary>زمان تقریبی بازگشت (دقیقه، ۵ تا ۱۴۴۰) برای سربرگ Retry-After که به گوگل می‌گوید بعداً برگردد.</summary>
    public int MaintenanceRetryMinutes { get; set; } = 60;

    /// <summary>IPهایی که در حالت تعمیر سایت را عادی می‌بینند (با کاما یا خط جدا). مثال: «5.6.7.8, 10.0.0.0/8».</summary>
    [MaxLength(1000)]
    public string? MaintenanceAllowedIps { get; set; }

    /// <summary>مسدودسازی خودکار IPهایی که در یک ساعت بیش از IpAutoBlockThreshold بار ورود ناموفق داشته‌اند.</summary>
    public bool IsIpAutoBlockEnabled { get; set; }

    /// <summary>آستانهٔ تلاش ناموفق ورود در یک ساعت (۵ تا ۱۰۰۰). مثال: ۲۰.</summary>
    public int IpAutoBlockThreshold { get; set; } = 20;

    /// <summary>مدت مسدودی خودکار (ساعت، ۱ تا ۷۲۰).</summary>
    public int IpAutoBlockHours { get; set; } = 24;

    /// <summary>فایل‌های لاگ روزانه (App_Data/logs) چند روز نگه داشته شوند (۱ تا ۱۸۰).</summary>
    public int LogFileRetentionDays { get; set; } = 14;

    /// <summary>
    /// کلید پایش بیرونی: با /healthz?token=… همهٔ بررسی‌های سلامت (نه فقط پایگاه داده) به‌صورت JSON برمی‌گردد.
    /// خالی = فقط وضعیت کلی. حداقل ۱۶ نویسه.
    /// </summary>
    [MaxLength(100)]
    public string? HealthCheckToken { get; set; }

    // ── امنیت کارکنان (فاز ۱۰) – منطق: Core/Services/Team ──

    /// <summary>کارکنان بتوانند برنامهٔ احراز هویت (Google Authenticator و …) را برای ورود دومرحله‌ای فعال کنند.</summary>
    public bool IsStaffTotpEnabled { get; set; } = true;

    /// <summary>
    /// برنامهٔ احراز هویت برای همهٔ کارکنان اجباری باشد: کارمندی که فعالش نکرده با ورود به پنل اول به صفحهٔ راه‌اندازی می‌رود.
    /// فقط وقتی IsStaffTotpEnabled روشن است اثر دارد.
    /// </summary>
    public bool IsStaffTotpRequired { get; set; }

    /// <summary>برای کارهای حساس (ذخیرهٔ تنظیمات، تغییر نقش، کیف پول، برداشت، پشتیبان، فایروال) دوباره رمز یا کد خواسته شود.</summary>
    public bool IsReauthEnabled { get; set; }

    /// <summary>بعد از تأیید دوباره، تا چند دقیقه کار حساس بدون پرسش مجاز است (۵ تا ۲۴۰). مثال: ۱۵.</summary>
    public int ReauthMinutes { get; set; } = 15;

    /// <summary>«تأیید دونفره»: کارهای حساس بالای سقف‌ها فقط بعد از تأیید یک کارمند دیگر اجرا می‌شوند.</summary>
    public bool IsTwoPersonApprovalEnabled { get; set; }

    /// <summary>تغییر دستی کیف پول از این مبلغ (تومان، قدر مطلق) به بالا تأیید دوم می‌خواهد. ۰ = همهٔ تغییرها.</summary>
    public decimal TwoPersonWalletThreshold { get; set; } = 1_000_000;

    /// <summary>پرداخت درخواست برداشت از این مبلغ (تومان) به بالا تأیید دوم می‌خواهد. ۰ = همهٔ برداشت‌ها.</summary>
    public decimal TwoPersonWithdrawalThreshold { get; set; } = 5_000_000;

    /// <summary>تغییر نقش‌های کارکنان (و مالک کردن) هم تأیید دوم بخواهد.</summary>
    public bool IsTwoPersonRoleChangeEnabled { get; set; } = true;

    /// <summary>
    /// پوشاندن اطلاعات شخصی مشتریان (موبایل، ایمیل، نشانی، شبا و کارت) در پنل برای کارکنانی که دسترسی
    /// «دیدن اطلاعات کامل مشتری» ندارند. مثال: 09121234567 ← ۰۹۱۲•••۴۵۶۷.
    /// </summary>
    public bool IsDataMaskingEnabled { get; set; }

    /// <summary>Generated automatically (base64url P-256 private key). Never exposed to the storefront.</summary>
    [MaxLength(200)]
    public string? VapidPrivateKey { get; set; }

    // ════════════════════════════════════════════════════════════════════
    //  مرحلهٔ دهم – برند قابل‌تنظیم (لوگو = آیکون + متن HTML)
    //  نمایش: Pages/Shared/_BrandLockup.cshtml و SiteContext.Brand*؛ تنظیم: پنل ← تنظیمات ← «برند و لوگو».
    //  آیکون «نشان» (LogoUrl) همیشه بدون متن است و نام فروشگاه به صورت متن واقعی (نه داخل تصویر) کنارش نوشته می‌شود:
    //  «فراتک» با رنگ متن و «شاپ» با رنگ تأکیدی. مثال خروجی: [آیکون] فراتک <span style="color:#0069FD">شاپ</span>
    // ════════════════════════════════════════════════════════════════════

    /// <summary>آیکون نشان برای زمینهٔ تیره (حالت شب سایت و نوار کناری پنل). خالی = نسخهٔ سفید نشان برند یا همان LogoUrl.</summary>
    [MaxLength(500)]
    public string? LogoDarkUrl { get; set; }

    /// <summary>آیکون اپ (PWA) و صفحهٔ اصلی گوشی؛ تصویر مربع حداقل ۵۱۲×۵۱۲. خالی = آیکون مشکی برند با نشان فراتک (بدون متن).</summary>
    [MaxLength(500)]
    public string? PwaIconUrl { get; set; }

    /// <summary>بخش اول متن برند. خالی = «فراتک». مثال: «فراتک».</summary>
    [MaxLength(40)]
    public string? BrandTextPrimary { get; set; }

    /// <summary>بخش دوم (رنگی) متن برند. خالی = «شاپ». برای حذف بخش دوم یک خط تیره «-» بنویسید.</summary>
    [MaxLength(40)]
    public string? BrandTextAccent { get; set; }

    /// <summary>رنگ بخش اول روی زمینهٔ روشن (hex). خالی = سرمه‌ای برند؛ روی زمینهٔ تیره همیشه سفید است تا خوانا بماند.</summary>
    [MaxLength(9)]
    public string? BrandTextColor { get; set; }

    /// <summary>رنگ بخش دوم (hex). خالی = آبی برند #0069FD.</summary>
    [MaxLength(9)]
    public string? BrandAccentColor { get; set; }

    /// <summary>متن برند کنار آیکون در هدر سایت. پیش‌فرض خاموش: هدر فقط آیکون را نشان می‌دهد.</summary>
    public bool BrandTextInHeader { get; set; }

    /// <summary>متن برند در صفحه‌های ورود و ثبت‌نام.</summary>
    public bool BrandTextInAuth { get; set; } = true;

    /// <summary>متن برند در سربرگ پنل مدیریت.</summary>
    public bool BrandTextInAdmin { get; set; } = true;

    /// <summary>متن برند در فاکتور (و PDF آن).</summary>
    public bool BrandTextInInvoice { get; set; } = true;

    /// <summary>
    /// مرحلهٔ دوازدهم – «استودیوی لوگو»: تنظیم جداگانهٔ لوگو برای هر محل (هدر، منوی موبایل، فوتر، ورود، پنل، فاکتور، ایمیل،
    /// صفحهٔ تعمیرات) و آیکون‌های اپ، به صورت JSON. ساختار و اعتبارسنجی: Core/Services/Branding/BrandStyle.cs.
    /// مثال: {"tagline":"خرید آنلاین لوازم جانبی","places":{"Header":{"iconHeight":48,"iconHeightMobile":36,"showText":true,"fontSize":26,"iconGap":10,"partGap":6}}}
    /// خالی = همهٔ محل‌ها با ظاهر پیش‌فرض (همان ظاهر مرحلهٔ دهم).
    /// </summary>
    public string? BrandStyleJson { get; set; }

    // ════════════════════════════════════════════════════════════════════
    //  مرحلهٔ دهم – سیاست قیمت‌گذاری خودکار وقتی قیمت خرید از تأمین‌کننده عوض می‌شود
    //  موتور: Core/Services/Suppliers/SupplierRepricing.cs (محاسبهٔ خالص و تست‌پذیر).
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// اعمال خودکار تغییر قیمت خرید: ۰ = همیشه با تأیید کارمند (مثل قبل)، ۱ = افزایش‌ها خودکار (کاهش‌ها با تأیید)،
    /// ۲ = همهٔ تغییرها خودکار. پیش‌فرض ۱ تا اگر کسی یادش رفت تأیید بزند، با قیمت قدیم زیان نکنیم.
    /// </summary>
    public int SupplierPriceAutoMode { get; set; } = 1;

    /// <summary>
    /// روش محاسبهٔ قیمت فروش تازه (SupplierRepriceMode): ۰ = فقط قیمت خرید به‌روز شود، ۱ = حفظ درصد سود،
    /// ۲ = حفظ مبلغ سود، ۳ = درصد ثابت روی خرید، ۴ = مبلغ ثابت روی خرید، ۵ = بیشترینِ «حفظ درصد» و «حفظ مبلغ» (محتاطانه‌ترین).
    /// مثال (خرید ۲۰۰→۴۰۰، فروش ۳۰۰): حالت ۱ → ۶۰۰، حالت ۲ → ۵۰۰.
    /// </summary>
    public int SupplierRepriceMode { get; set; } = 1;

    /// <summary>درصد سود ثابت روی قیمت خرید در حالت ۳. مثال: ۲۵ ← خرید ۴۰۰ → فروش ۵۰۰.</summary>
    public int SupplierRepriceMarkupPercent { get; set; } = 25;

    /// <summary>مبلغ سود ثابت (تومان) روی قیمت خرید در حالت ۴. مثال: ۵۰٬۰۰۰ ← خرید ۴۰۰ هزار → فروش ۴۵۰ هزار.</summary>
    public decimal SupplierRepriceMarkupAmount { get; set; }

    /// <summary>حداقل سود مجاز (درصد روی خرید)؛ قیمت فروش هیچ‌وقت از خرید × (۱ + این درصد) کمتر نمی‌شود. ۰ = فقط زیان نکند.</summary>
    public int SupplierRepriceMinMarginPercent { get; set; } = 5;

    /// <summary>گرد کردن قیمت تازه به بالا (تومان). مثال: ۱۰۰۰ ← ۵۱۸٬۷۵۰ → ۵۱۹٬۰۰۰. ۰ = بدون گرد کردن.</summary>
    public decimal SupplierRepriceRounding { get; set; } = 1000;

    /// <summary>وقتی قیمت خرید کم شد قیمت فروش هم با همان روش پایین بیاید؟ خاموش = قیمت فروش ثابت می‌ماند (سود بیشتر).</summary>
    public bool SupplierRepriceOnDecrease { get; set; }

    /// <summary>
    /// سقف افزایش خودکار (درصد). افزایش بیشتر از این (شاید خطای خواندن سایت منبع) خودکار اعمال نمی‌شود؛
    /// به‌جایش کالا موقتاً ناموجود می‌شود تا با قیمت زیان‌ده فروخته نشود و پیشنهاد برای تأیید می‌ماند. ۰ = بدون سقف.
    /// </summary>
    public int SupplierRepriceMaxJumpPercent { get; set; } = 50;

    // ════════════════════════════════════════════════════════════════════
    //  مرحلهٔ دهم – «پیشنهاد قیمت فروش» در فرم محصول
    //  موتور: Core/Services/Pricing/PriceAdvisor.cs؛ قیمت رقبا: Core/Services/Pricing/CompetitorPrices.cs.
    //  همهٔ هزینه‌ها برای هر عدد کالا حساب می‌شوند، چه روی محصول ثبت شده باشند چه نه.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>هزینهٔ بسته‌بندی هر عدد (تومان). مثال: ۱۵٬۰۰۰.</summary>
    public decimal PricingPackagingCost { get; set; }

    /// <summary>سهم ارسالی که فروشگاه می‌پردازد برای هر عدد (تومان) – مثلاً ارسال رایگان یا کرایهٔ رسیدن کالا از تأمین‌کننده.</summary>
    public decimal PricingShippingCost { get; set; }

    /// <summary>کارمزد درگاه پرداخت (درصد از قیمت فروش). مثال: ۱.</summary>
    public decimal PricingGatewayFeePercent { get; set; } = 1;

    /// <summary>هزینه‌های دیگر به درصد از قیمت فروش (تبلیغات، مرجوعی، تخفیف‌ها). مثال: ۳.</summary>
    public decimal PricingOtherCostPercent { get; set; }

    /// <summary>هزینه‌های ثابت دیگر برای هر عدد (تومان) – مثل سهم اجاره و حقوق.</summary>
    public decimal PricingOtherCostAmount { get; set; }

    /// <summary>مالیات بر ارزش افزوده که روی قیمت فروش می‌آید (درصد). ۰ = مشمول نیست.</summary>
    public decimal PricingTaxPercent { get; set; }

    /// <summary>سود خالص هدف (درصد روی کل هزینهٔ تمام‌شده). مثال: ۳۰.</summary>
    public int PricingTargetMarginPercent { get; set; } = 30;

    /// <summary>کمترین سود خالص قابل‌قبول (درصد)؛ پیشنهاد هیچ‌وقت پایین‌تر از این نمی‌رود حتی اگر رقبا ارزان‌تر باشند.</summary>
    public int PricingMinMarginPercent { get; set; } = 8;

    /// <summary>چند درصد ارزان‌تر از ارزان‌ترین رقیب معتبر پیشنهاد شود. مثال: ۲ ← رقیب ۲۷۰ → ۲۶۴٫۶.</summary>
    public decimal PricingUndercutPercent { get; set; } = 2;

    /// <summary>گرد کردن قیمت پیشنهادی (تومان). مثال: ۱۰۰۰.</summary>
    public decimal PricingRounding { get; set; } = 1000;

    /// <summary>جست‌وجوی خودکار قیمت همین کالا در سایت‌های دیگر (ترب و دیجی‌کالا؛ رایگان، بدون کلید).</summary>
    public bool IsCompetitorPriceLookupEnabled { get; set; } = true;
}
