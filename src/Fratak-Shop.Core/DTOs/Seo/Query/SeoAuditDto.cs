using Fratak_Shop.Core.SeoEngine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Seo;

namespace Fratak_Shop.Core.DTOs.Seo.Query;

/// <summary>
/// Stored result of an «سئوی هوشمند» run, ready to be reviewed by the specialist.
/// The proposal is never applied automatically: the operator edits the fields he
/// wants and then confirms the change.
/// </summary>
public class SeoAuditDto : BaseDto
{
    public SeoEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string EntityTitle { get; set; } = string.Empty;
    public string? EntityUrl { get; set; }
    public string? EditUrl { get; set; }
    public int Score { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public SeoAuditStatus Status { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }

    public SeoAuditResult Result { get; set; } = new();

    public List<SeoFinding> Findings => Result.Findings;
    public SeoProposal Proposal => Result.Proposal;

    public string StatusTitle => Status switch
    {
        SeoAuditStatus.PendingReview => "در انتظار بررسی",
        SeoAuditStatus.Applied => "اعمال‌شده",
        SeoAuditStatus.Rejected => "ردشده",
        SeoAuditStatus.Stale => "منقضی",
        _ => "نامشخص",
    };
}

public class SeoAuditFilterParams : BaseFilterParam
{
    public string? Search { get; set; }
    public SeoEntityType? EntityType { get; set; }
    public SeoAuditStatus? Status { get; set; }
    public int? MaxScore { get; set; }
}

public class SeoAuditFilterResult : BaseFilter<SeoAuditDto, SeoAuditFilterParams>;

/// <summary>خلاصه وضعیت سئوی سایت برای داشبورد مرکز سئو.</summary>
public class SeoDashboardDto
{
    public int ContentCount { get; set; }
    public int OptimizedCount { get; set; }
    public int MissingTitleCount { get; set; }
    public int MissingDescriptionCount { get; set; }
    public int LowScoreCount { get; set; }
    public int NotReviewedCount { get; set; }
    public int PendingAudits { get; set; }
    public int RedirectCount { get; set; }
    public int NotFoundCount { get; set; }
    public int VisitsLastThirtyDays { get; set; }
    public double AverageScore { get; set; }
    public List<SeoContentScoreDto> WorstContent { get; set; } = [];
    public List<SeoContentScoreDto> MissingSeoContent { get; set; } = [];
    public List<KeywordIdeaDto> KeywordIdeas { get; set; } = [];
}

public class SeoContentScoreDto
{
    public SeoEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? EditUrl { get; set; }
    public int? Score { get; set; }
    public bool HasTitle { get; set; }
    public bool HasDescription { get; set; }
    public bool IsReviewed { get; set; }
    public string EntityTypeTitle => EntityType switch
    {
        SeoEntityType.Product => "محصول",
        SeoEntityType.Category => "دسته‌بندی",
        SeoEntityType.BlogPost => "مقاله",
        SeoEntityType.BlogCategory => "دسته وبلاگ",
        SeoEntityType.Page => "صفحه",
        _ => "محتوا",
    };
}

public class KeywordIdeaDto
{
    public string Keyword { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>خروجی «هوشمندسازی نوشته‌ها» برای بازبینی انسانی پیش از ذخیره.</summary>
public class ContentEnhancementDto
{
    public SeoEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string EntityTitle { get; set; } = string.Empty;
    public string? OriginalShortDescription { get; set; }
    public string? EnhancedShortDescription { get; set; }
    public string? OriginalContent { get; set; }
    public string? EnhancedContent { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string FocusKeyword { get; set; } = string.Empty;
    public List<SeoFaqSuggestion> FaqSuggestions { get; set; } = [];
    public List<SeoLinkSuggestion> InternalLinks { get; set; } = [];
}
