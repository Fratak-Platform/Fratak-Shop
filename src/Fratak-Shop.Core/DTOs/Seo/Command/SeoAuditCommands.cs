using Fratak_Shop.Core.SeoEngine;

namespace Fratak_Shop.Core.DTOs.Seo.Command;

/// <summary>
/// Confirmation of a smart-SEO proposal. Only the fields the operator approved are
/// written; <see cref="ApplySlug"/> and <see cref="ApplyContent"/> are opt-in because
/// changing a URL or the body of a page has consequences.
/// </summary>
public class ApplySeoProposalCommand
{
    public Guid RecordId { get; set; }

    public SeoProposal Proposal { get; set; } = new();

    public bool ApplySlug { get; set; }
    public bool ApplyContent { get; set; }
    public bool ApplyImageAlts { get; set; } = true;
    public bool MarkAsReviewed { get; set; } = true;
    public Guid? UserId { get; set; }
}

public class RejectSeoProposalCommand
{
    public Guid RecordId { get; set; }
    public string? Reason { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>ذخیره مستقیم فیلدهای سئو از فرم ویرایش (بدون اجرای موتور هوشمند).</summary>
public class SaveSeoFormCommand
{
    public Data.Utilities.SeoManagement.SeoMetaData Seo { get; set; } = new();
    public string? Slug { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>اعمال خروجی هوشمندسازی نوشته‌ها روی محصول یا مقاله.</summary>
public class ApplyContentEnhancementCommand
{
    public Data.Entities.Seo.SeoEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string? ShortDescription { get; set; }
    public string? Content { get; set; }
    public Guid? UserId { get; set; }
}
