using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Core.DTOs.Pages.Command;

/// <summary>
/// دستور ایجاد اطلاعات سئوی یک صفحه سایت. هر صفحه فقط یک رکورد سئو دارد.
/// </summary>
public class CreatePageSeoCommand
{
    public SitePage Page { get; set; }
    public SeoMetaData? Seo { get; set; }
}

/// <summary>
/// دستور ویرایش اطلاعات سئوی یک صفحه سایت.
/// </summary>
public class EditPageSeoCommand
{
    public Guid Id { get; set; }
    public SitePage Page { get; set; }
    public SeoMetaData? Seo { get; set; }
}

/// <summary>
/// دستور ثبت یا به‌روزرسانی سئوی صفحه (اگر صفحه رکورد سئو داشته باشد ویرایش و
/// در غیر این صورت ایجاد می‌شود). مناسب فرم مدیریت سئو که یک صفحه را ذخیره می‌کند.
/// </summary>
public class SavePageSeoCommand
{
    public SitePage Page { get; set; }
    public SeoMetaData? Seo { get; set; }
}
