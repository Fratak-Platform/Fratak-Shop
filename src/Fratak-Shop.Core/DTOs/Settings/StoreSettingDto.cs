namespace Fratak_Shop.Core.DTOs.Settings;

/// <summary>
/// Editable store settings (admin panel form). See <c>StoreSetting</c> for the meaning of each field.
/// </summary>
public class StoreSettingCommand
{
    public string SiteName { get; set; } = null!;
    public string? Tagline { get; set; }
    public string? SiteDescription { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? SupportPhone { get; set; }
    public string? SupportEmail { get; set; }
    public string? Address { get; set; }
    public string? PostalCode { get; set; }
    public string? WorkingHours { get; set; }
    public string? MapEmbedUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? TelegramUrl { get; set; }
    public string? WhatsAppUrl { get; set; }
    public string? LinkedinUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? AparatUrl { get; set; }
    public string? YoutubeUrl { get; set; }
    public string? FooterTitle { get; set; }
    public string? FooterSubtitle { get; set; }
    public string? FooterText { get; set; }
    public string? CopyrightText { get; set; }
    public decimal FreeShippingThreshold { get; set; }
    public decimal MinimumOrderAmount { get; set; }
    public int DeliveryDaysAhead { get; set; } = 6;
    public string? DeliveryOffDays { get; set; } = "5";
    public int SameDayLeadMinutes { get; set; } = 120;
    public int UnpaidOrderTimeoutMinutes { get; set; } = 60;
    public bool AllowCustomerCancellation { get; set; } = true;
    public bool IsOnlinePaymentEnabled { get; set; } = true;
    public bool IsCashOnDeliveryEnabled { get; set; } = true;
    public decimal CashOnDeliveryMaxAmount { get; set; }
    public string? ZarinPalMerchantId { get; set; }
    public bool ZarinPalSandbox { get; set; } = true;
    public bool IsWalletEnabled { get; set; } = true;
    public bool IsWalletPaymentEnabled { get; set; } = true;
    public bool IsWalletTopUpEnabled { get; set; } = true;
    public bool IsWithdrawalEnabled { get; set; } = true;
    public decimal WalletMinTopUpAmount { get; set; } = 10_000;
    public decimal WalletMaxTopUpAmount { get; set; } = 100_000_000;
    public decimal WalletMinWithdrawalAmount { get; set; } = 50_000;
    public string TitleSeparator { get; set; } = "|";
    public string? DefaultMetaKeywords { get; set; }
    public string? DefaultOgImageUrl { get; set; }
    public string? GoogleSiteVerification { get; set; }
    public string? BingSiteVerification { get; set; }
    public string? RobotsTxtExtra { get; set; }
    public string? OrganizationLegalName { get; set; }
}

/// <summary>Store settings as read by the storefront (cached).</summary>
public class StoreSettingDto : StoreSettingCommand
{
    public Guid Id { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Days of the week without delivery.</summary>
    public IReadOnlySet<DayOfWeek> OffDays =>
        (DeliveryOffDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var day) && day is >= 0 and <= 6 ? (DayOfWeek?)day : null)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .ToHashSet();

    public bool HasZarinPal => IsOnlinePaymentEnabled && !string.IsNullOrWhiteSpace(ZarinPalMerchantId);

    /// <summary>Cash on delivery is enabled and allowed for the given payable amount.</summary>
    public bool AllowsCashOnDelivery(decimal payableAmount) =>
        IsCashOnDeliveryEnabled && (CashOnDeliveryMaxAmount <= 0 || payableAmount <= CashOnDeliveryMaxAmount);

    public bool CanPayWithWallet => IsWalletEnabled && IsWalletPaymentEnabled;

    public bool CanTopUpWallet => IsWalletEnabled && IsWalletTopUpEnabled && HasZarinPal;

    public bool CanWithdraw => IsWalletEnabled && IsWithdrawalEnabled;
}
