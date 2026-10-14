using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.SeoRedirects;

namespace Fratak_Shop.Core.DTOs.SeoRedirects.Query;

/// <summary>
/// پارامترهای فیلتر لیست ریدایرکت‌ها (مدیریت سئو).
/// </summary>
public class SeoRedirectFilterParams : BaseFilterParam
{
    /// <summary>جستجو در مسیر مبدأ، مقصد و یادداشت‌ها.</summary>
    public string? Search { get; set; }

    public RedirectType? Type { get; set; }

    public bool? IsActive { get; set; }
}

public class SeoRedirectFilterResult : BaseFilter<SeoRedirectDto, SeoRedirectFilterParams>;
