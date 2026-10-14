using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.Mappers.Pages;

public static class PageSeoMapper
{
    public static PageSeoDto Map(this PageSeo pageSeo)
    {
        return new PageSeoDto
        {
            Id = pageSeo.Id,
            CreatedAt = pageSeo.CreatedAt,
            UpdatedAt = pageSeo.UpdatedAt,
            IsDeleted = pageSeo.IsDeleted,
            DeletedAt = pageSeo.DeletedAt,
            Page = pageSeo.Page,
            Seo = pageSeo.Seo.MapSeoMetaData(),
        };
    }

    public static List<PageSeoDto> Map(this List<PageSeo> pageSeos)
    {
        return pageSeos.Select(pageSeo => pageSeo.Map()).ToList();
    }
}
