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

    // ── Progressive web app ──────────────────────────────────────────────
    public bool IsPwaEnabled { get; set; } = true;

    [MaxLength(30)]
    public string? PwaShortName { get; set; }

    /// <summary>Browser / app theme colour (hex).</summary>
    [MaxLength(20)]
    public string? ThemeColor { get; set; }
}
