using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Core.DTOs.Banners.Query;

public class BannerDto : BaseDto
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? ImageName { get; set; }
    public string? MobileImageName { get; set; }
    public string? ImageAlt { get; set; }
    public string? ButtonText { get; set; }
    public string? LinkUrl { get; set; }
    public BannerPosition Position { get; set; }
    public int DisplayOrder { get; set; }
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? EndAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>دور ۲۳: کالای اصلی بنر (ناموجود/غیرفعال ← بنر خودکار پنهان).</summary>
    public Guid? ProductId { get; set; }

    /// <summary>دور ۲۳: دستهٔ «سربرگ دسته».</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>دور ۲۳: گروه نسخهٔ آزمون A/B (خالی = بنر عادی).</summary>
    public string? VariantGroup { get; set; }

    /// <summary>دور ۲۳: تعداد نمایش.</summary>
    public int Views { get; set; }

    /// <summary>دور ۲۳: تعداد کلیک.</summary>
    public int Clicks { get; set; }
}