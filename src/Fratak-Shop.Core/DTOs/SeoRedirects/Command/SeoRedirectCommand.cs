using Fratak_Shop.Data.Entities.SeoRedirects;

namespace Fratak_Shop.Core.DTOs.SeoRedirects.Command;

/// <summary>
/// دستور ایجاد ریدایرکت سئو (مثلاً پس از تغییر اسلاگ محصول یا مقاله).
/// </summary>
public class CreateSeoRedirectCommand
{
    public string FromPath { get; set; } = null!;
    public string ToUrl { get; set; } = null!;
    public RedirectType Type { get; set; } = RedirectType.MovedPermanently;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// دستور ویرایش ریدایرکت سئو.
/// </summary>
public class EditSeoRedirectCommand
{
    public Guid Id { get; set; }
    public string FromPath { get; set; } = null!;
    public string ToUrl { get; set; } = null!;
    public RedirectType Type { get; set; } = RedirectType.MovedPermanently;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}
