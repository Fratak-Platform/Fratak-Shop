using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Contents;

public class ContentPageDto : BaseDto
{
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }

    /// <summary>Sanitized HTML body.</summary>
    public string? Content { get; set; }

    public string? ImageUrl { get; set; }
    public SitePage? PageKey { get; set; }
    public bool IsActive { get; set; }
    public bool ShowInFooter { get; set; }
    public int DisplayOrder { get; set; }
    public SeoMetaData? Seo { get; set; }
}

/// <summary>Light link to a content page (footer, sitemap).</summary>
public class ContentPageLinkDto
{
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public bool ShowInFooter { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool NoIndex { get; set; }
}

public class ContentPageCommand
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public string? ImageUrl { get; set; }
    public SitePage? PageKey { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowInFooter { get; set; } = true;
    public int DisplayOrder { get; set; }
    public SeoMetaData? Seo { get; set; }
}

public class SiteFeatureDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string? IconKey { get; set; }
    public string? LinkUrl { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class TrustBadgeDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = null!;
    public string ImageUrl { get; set; } = null!;
    public string? LinkUrl { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class HomeSectionDto
{
    public Guid Id { get; set; }
    public HomeSectionType Type { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? LinkUrl { get; set; }
    public int ItemCount { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategorySlug { get; set; }
    public string? CategoryTitle { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}
