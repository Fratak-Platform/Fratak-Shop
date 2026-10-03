using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Payments;

/// <summary>
/// Destination bank card of the shop, shown to the customer in the
/// «کارت به کارت» payment step. Several cards can be published at the same time
/// (different banks or accounts) and the customer picks one.
/// </summary>
public class PaymentCard : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string BankName { get; set; } = null!;

    /// <summary>
    /// 16-digit card number. Stored as typed (digits only) and masked in the UI.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string CardNumber { get; set; } = null!;

    /// <summary>
    /// Account holder name printed on the card.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string AccountHolder { get; set; } = null!;

    [MaxLength(26)]
    public string? ShebaNumber { get; set; }

    [MaxLength(200)]
    public string? BranchName { get; set; }

    /// <summary>
    /// Extra instructions shown under the card (transfer deadline, etc.).
    /// </summary>
    [MaxLength(1000)]
    public string? Instructions { get; set; }

    /// <summary>
    /// Only one card can be the default suggestion of the checkout page.
    /// </summary>
    public bool IsDefault { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
