using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Audit;

/// <summary>
/// Administrative audit trail: who changed what and when. Every write performed
/// from the admin area is logged, which is required for a multi-role panel
/// (product manager, SEO specialist, support agent, ...).
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? UserId { get; set; }

    /// <summary>
    /// Machine action key, e.g. <c>Product.Update</c> or <c>Seo.ApplySmartSeo</c>.
    /// </summary>
    [Required]
    [MaxLength(150)]
    public string Action { get; set; } = null!;

    [MaxLength(100)]
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
