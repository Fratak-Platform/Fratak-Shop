using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// Everything the SEO engine needs about one piece of content. The service layer
/// fills it from the entity (product, post, category or page) so the engine itself
/// stays independent from the database.
/// </summary>
public class SeoAuditInput
{
    public SeoEntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>Name/title of the content.</summary>
    public string Title { get; set; } = string.Empty;

    public string? Slug { get; set; }

    /// <summary>Short marketing sentence (product summary or post excerpt).</summary>
    public string? ShortDescription { get; set; }

    /// <summary>Main body, HTML allowed.</summary>
    public string? Content { get; set; }

    public string? ImageAlt { get; set; }

    public int ImageCount { get; set; }

    public string? FocusKeyword { get; set; }

    public List<string> SecondaryKeywords { get; set; } = [];

    public string? CategoryName { get; set; }

    public string? BrandName { get; set; }

    public decimal? Price { get; set; }

    /// <summary>Average rating (1..5) used inside the product structured data.</summary>
    public double? RatingValue { get; set; }

    public int RatingCount { get; set; }

    public string? StockStatus { get; set; }

    /// <summary>Product specifications, used for the generated copy and FAQ.</summary>
    public Dictionary<string, string> Features { get; set; } = [];

    /// <summary>Other pages of the shop that may be linked from this content.</summary>
    public List<SeoLinkSuggestion> LinkCandidates { get; set; } = [];

    public SeoMetaData? Existing { get; set; }

    /// <summary>Canonical address of the shop, e.g. https://fratak.ir</summary>
    public string SiteUrl { get; set; } = string.Empty;

    public string SiteName { get; set; } = "فراتک";

    public string? Language { get; set; } = "fa-IR";
}

/// <summary>Internal link suggestion (title + address of another page).</summary>
public class SeoLinkSuggestion
{
    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? AnchorText { get; set; }

    public SeoEntityType EntityType { get; set; }
}

/// <summary>One finding of the audit — a rule, its weight and how the content scores.</summary>
public class SeoFinding
{
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public SeoFindingSeverity Severity { get; set; }

    public int MaxPoints { get; set; }

    public int Points { get; set; }

    public string? Suggestion { get; set; }

    public bool IsPassed => Points >= MaxPoints;
}

public enum SeoFindingSeverity
{
    /// <summary>مشکل جدی که باید رفع شود</summary>
    Critical = 1,
    /// <summary>قابل بهبود</summary>
    Warning = 2,
    /// <summary>در وضعیت مطلوب</summary>
    Good = 3,
}

/// <summary>Field by field proposal produced by the smart SEO run.</summary>
public class SeoProposal
{
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? MetaKeywords { get; set; }
    public string? SecondaryKeywordsJson { get; set; }
    public string? FocusKeyword { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? BreadcrumbTitle { get; set; }
    public string? Slug { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public string? OgType { get; set; }
    public string? OgImageAlt { get; set; }
    public string? XCard { get; set; }
    public string? XTitle { get; set; }
    public string? XDescription { get; set; }
    public string? SchemaType { get; set; }
    public string? StructuredDataJsonLd { get; set; }
    public bool IncludeInSitemap { get; set; } = true;
    public decimal? SitemapPriority { get; set; }
    public string? SitemapChangeFrequency { get; set; }
    public string? ImageAlt { get; set; }
    public string? EnhancedShortDescription { get; set; }
    public string? EnhancedContent { get; set; }
    public string? Summary { get; set; }

    /// <summary>
    /// Keys of the fields that were empty in the content and are filled only by
    /// this proposal — the review screen highlights them for the specialist.
    /// </summary>
    public List<string> GeneratedFields { get; set; } = [];

    public List<SeoLinkSuggestion> InternalLinks { get; set; } = [];

    public List<SeoFaqSuggestion> FaqSuggestions { get; set; } = [];

    public List<string> KeywordIdeas { get; set; } = [];
}

public class SeoFaqSuggestion
{
    public string Question { get; set; } = string.Empty;

    public string Answer { get; set; } = string.Empty;
}

/// <summary>Result of a full audit: score, findings and the proposal.</summary>
public class SeoAuditResult
{
    public int Score { get; set; }

    public SeoAuditGrade Grade { get; set; }

    public List<SeoFinding> Findings { get; set; } = [];

    public SeoProposal Proposal { get; set; } = new();

    public string Summary { get; set; } = string.Empty;

    public int CriticalCount => Findings.Count(finding => finding.Severity == SeoFindingSeverity.Critical);

    public int WarningCount => Findings.Count(finding => finding.Severity == SeoFindingSeverity.Warning);
}

public enum SeoAuditGrade
{
    /// <summary>ضعیف — نیازمند بازنویسی</summary>
    Poor = 1,
    /// <summary>قابل قبول — جای بهبود دارد</summary>
    Fair = 2,
    /// <summary>خوب</summary>
    Good = 3,
    /// <summary>عالی</summary>
    Excellent = 4,
}
