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

    /// <summary>Background colour (hex, e.g. #0284c7).</summary>
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

    /// <summary>Generated automatically (base64url P-256 private key). Never exposed to the storefront.</summary>
    [MaxLength(200)]
    public string? VapidPrivateKey { get; set; }
}
