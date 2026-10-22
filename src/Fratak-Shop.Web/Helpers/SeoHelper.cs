using Fratak_Shop.Core.DTOs.Products.Query;

namespace Fratak_Shop.Web.Helpers;

public static class SeoHelper
{
    public static string BuildPageTitle(string pageTitle, string siteName = "هایپر استار")
        => string.IsNullOrWhiteSpace(pageTitle) ? siteName : $"{pageTitle} | {siteName}";

    public static Dictionary<string, string?> BuildMetaTags(
        string? seoTitle,
        string? metaDescription,
        string? canonical,
        string? ogImage,
        string requestUrl)
    {
        var tags = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(seoTitle)) tags["title"] = seoTitle;
        if (!string.IsNullOrWhiteSpace(metaDescription)) tags["description"] = metaDescription;
        if (!string.IsNullOrWhiteSpace(canonical)) tags["canonical"] = canonical;
        else tags["canonical"] = requestUrl;
        if (!string.IsNullOrWhiteSpace(ogImage)) tags["og:image"] = ogImage;
        return tags;
    }
}
