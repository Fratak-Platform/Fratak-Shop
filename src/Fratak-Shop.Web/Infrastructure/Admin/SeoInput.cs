using Fratak_Shop.Data.Utilities.SeoManagement;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// The SEO fields editable in the admin panel (products, categories, brands, blog, pages). Fields that are
/// not on the form are preserved: <see cref="ApplyTo"/> updates an existing <see cref="SeoMetaData"/>.
/// </summary>
public sealed class SeoInput
{
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? FocusKeyword { get; set; }
    public string? MetaKeywords { get; set; }
    public string? CanonicalUrl { get; set; }
    public string? BreadcrumbTitle { get; set; }
    public string? OgTitle { get; set; }
    public string? OgDescription { get; set; }
    public bool NoIndex { get; set; }
    public bool NoFollow { get; set; }

    public static SeoInput From(SeoMetaData? seo) => seo is null ? new SeoInput() : new SeoInput
    {
        Title = seo.Title,
        MetaDescription = seo.MetaDescription,
        FocusKeyword = seo.FocusKeyword,
        MetaKeywords = seo.MetaKeywords,
        CanonicalUrl = seo.CanonicalUrl,
        BreadcrumbTitle = seo.BreadcrumbTitle,
        OgTitle = seo.OgTitle,
        OgDescription = seo.OgDescription,
        NoIndex = seo.NoIndex == true,
        NoFollow = seo.NoFollow == true,
    };

    /// <summary>Validation message, or null.</summary>
    public string? Validate()
    {
        if (Title?.Trim().Length > 200 || FocusKeyword?.Trim().Length > 200 || BreadcrumbTitle?.Trim().Length > 200 || OgTitle?.Trim().Length > 200)
            return "عنوان سئو، عبارت کلیدی، عنوان مسیر و عنوان اشتراک‌گذاری حداکثر ۲۰۰ نویسه‌اند.";
        if (MetaDescription?.Trim().Length > 500 || MetaKeywords?.Trim().Length > 500 || OgDescription?.Trim().Length > 500)
            return "توضیحات متا و کلمات کلیدی حداکثر ۵۰۰ نویسه‌اند.";
        if (!string.IsNullOrWhiteSpace(CanonicalUrl))
        {
            var url = CanonicalUrl.Trim();
            var valid = url.Length <= 2048
                        && ((url.StartsWith('/') && !url.StartsWith("//"))
                            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)));
            if (!valid)
                return "آدرس Canonical باید با / شروع شود یا یک آدرس کامل http(s) باشد.";
        }
        return null;
    }

    /// <summary>Copies the form values onto <paramref name="seo"/> (created when null) and returns it.</summary>
    public SeoMetaData ApplyTo(SeoMetaData? seo)
    {
        seo ??= new SeoMetaData();
        seo.Title = Clean(Title);
        seo.MetaDescription = Clean(MetaDescription);
        seo.FocusKeyword = Clean(FocusKeyword);
        seo.MetaKeywords = Clean(MetaKeywords);
        seo.CanonicalUrl = Clean(CanonicalUrl);
        seo.BreadcrumbTitle = Clean(BreadcrumbTitle);
        seo.OgTitle = Clean(OgTitle);
        seo.OgDescription = Clean(OgDescription);
        seo.NoIndex = NoIndex ? true : null;
        seo.NoFollow = NoFollow ? true : null;
        return seo;
    }

    /// <summary>A copy (for services that replace the whole metadata object).</summary>
    public SeoMetaData ApplyToCopy(SeoMetaData? existing)
    {
        var copy = new SeoMetaData();
        if (existing is not null)
            foreach (var property in typeof(SeoMetaData).GetProperties().Where(p => p.CanRead && p.CanWrite))
                property.SetValue(copy, property.GetValue(existing));
        return ApplyTo(copy);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Model of the <c>_SeoFields</c> partial.</summary>
public sealed record SeoFieldsModel(string Prefix, SeoInput Seo, string PreviewUrl, string? TitleSourceSelector = null, string? DescriptionSourceSelector = null);
