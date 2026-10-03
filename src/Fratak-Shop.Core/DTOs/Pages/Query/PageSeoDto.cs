using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Pages.Query;

public class PageSeoDto : BaseDto
{
    public SitePage Page { get; set; }
    public SeoMetaData? Seo { get; set; }

    public bool HasSeo => Seo is not null &&
                          (!string.IsNullOrWhiteSpace(Seo.Title) || !string.IsNullOrWhiteSpace(Seo.MetaDescription));

    public int? Score => Seo?.SeoScore;
}

public class PageAlertDto : BaseDto
{
    public SitePage Page { get; set; }
    public AlertPosition Position { get; set; }
    public AlertType Type { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; }

    public bool IsVisible => IsActive &&
                             (!StartAt.HasValue || StartAt.Value <= DateTimeOffset.UtcNow) &&
                             (!EndAt.HasValue || EndAt.Value >= DateTimeOffset.UtcNow);

    public string AlertTypeTitle => Type switch
    {
        AlertType.Success => "موفقیت",
        AlertType.Warning => "هشدار",
        AlertType.Error => "خطا",
        _ => "اطلاع‌رسانی",
    };
}
