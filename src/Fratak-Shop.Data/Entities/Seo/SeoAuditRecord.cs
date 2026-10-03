using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Seo;

/// <summary>
/// Result of an «سئوی هوشمند» run. The generated proposal is stored here instead
/// of being written directly to the entity, so the SEO specialist can review,
/// edit, apply or reject it — exactly the review step required before saving.
/// </summary>
public class SeoAuditRecord : BaseEntity
{
    public SeoEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>
    /// Overall SEO score (0..100) computed for the proposal.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// JSON payload with every proposed field (title, description, keywords,
    /// OG data, schema, internal links, ...) plus the audit findings.
    /// </summary>
    public string PayloadJson { get; set; } = null!;

    /// <summary>
    /// Short Persian summary shown in the review screen.
    /// </summary>
    [MaxLength(1000)]
    public string? Summary { get; set; }

    /// <summary>
    /// Provider that produced the proposal (heuristics engine or an AI provider).
    /// </summary>
    [MaxLength(100)]
    public string Provider { get; set; } = "FratakSeoEngine";

    public SeoAuditStatus Status { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public Guid? AppliedByUserId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public User? AppliedByUser { get; set; }

    public User? CreatedByUser { get; set; }
}

public enum SeoAuditStatus
{
    None = 0,
    /// <summary>پیشنهاد تولید شده و در انتظار بررسی</summary>
    PendingReview = 1,
    /// <summary>روی محتوا اعمال شده است</summary>
    Applied = 2,
    /// <summary>توسط کارشناس سئو رد شده است</summary>
    Rejected = 3,
    /// <summary>پیش‌نویس قدیمی که دیگر معتبر نیست</summary>
    Stale = 4,
}
