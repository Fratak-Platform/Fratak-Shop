using Fratak_Shop.Core.DTOs.SeoRedirects.Query;
using Fratak_Shop.Data.Entities.SeoRedirects;

namespace Fratak_Shop.Core.Mappers.SeoRedirects;

public static class SeoRedirectMapper
{
    public static SeoRedirectDto Map(this SeoRedirect redirect)
    {
        return new SeoRedirectDto
        {
            Id = redirect.Id,
            CreatedAt = redirect.CreatedAt,
            UpdatedAt = redirect.UpdatedAt,
            IsDeleted = redirect.IsDeleted,
            DeletedAt = redirect.DeletedAt,
            FromPath = redirect.FromPath,
            ToUrl = redirect.ToUrl,
            Type = redirect.Type,
            Notes = redirect.Notes,
            IsActive = redirect.IsActive,
        };
    }

    public static List<SeoRedirectDto> Map(this List<SeoRedirect> redirects)
    {
        return redirects.Select(redirect => redirect.Map()).ToList();
    }
}
