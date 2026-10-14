using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.DTOs.Pages.Query;

/// <summary>
/// اطلاعات سئوی یک صفحه مستقل سایت (صفحه اصلی، تماس با ما، سوالات متداول و ...).
/// </summary>
public class PageSeoDto : BaseDto
{
    public SitePage Page { get; set; }
    public SeoMetaDataDto? Seo { get; set; }
}
