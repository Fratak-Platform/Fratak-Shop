using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoRedirects;

namespace Fratak_Shop.Core.DTOs.SeoRedirects.Query;

/// <summary>
/// ریدایرکت ثبت‌شده برای یک مسیر قدیمی.
/// </summary>
public class SeoRedirectDto : BaseDto
{
    public string FromPath { get; set; } = null!;
    public string ToUrl { get; set; } = null!;
    public RedirectType Type { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}
