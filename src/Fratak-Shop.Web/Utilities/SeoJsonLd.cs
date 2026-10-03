using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fratak_Shop.Core.DTOs.Products.Query;

namespace Fratak_Shop.Web.Utilities;

/// <summary>
/// Structured data produced by the pages themselves, so even content that has not
/// been touched by the SEO specialist still ships valid schema.org markup:
/// Product + Offer + AggregateRating on product pages, BlogPosting on posts,
/// FAQPage on the FAQ page and BreadcrumbList wherever a breadcrumb is rendered.
/// </summary>
public static class SeoJsonLd
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Product(ProductDetailDto product, string siteUrl, string siteName, string imageUrl,
        double ratingValue, int ratingCount)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = product.Name,
            ["description"] = string.IsNullOrWhiteSpace(product.ShortDescription) ? product.Name : product.ShortDescription,
            ["sku"] = product.SKU,
            ["image"] = imageUrl,
            ["url"] = $"{siteUrl}/product/{product.Slug}",
            ["brand"] = string.IsNullOrWhiteSpace(product.CategoryName)
                ? new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = siteName }
                : new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = product.CategoryName },
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["price"] = product.FinalPrice,
                ["priceCurrency"] = "IRR",
                ["availability"] = product.StockQuantity > 0
                    ? "https://schema.org/InStock"
                    : "https://schema.org/OutOfStock",
                ["url"] = $"{siteUrl}/product/{product.Slug}",
                ["seller"] = new Dictionary<string, object?> { ["@type"] = "Organization", ["name"] = siteName },
            },
        };

        if (ratingCount > 0 && ratingValue > 0)
        {
            data["aggregateRating"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = ratingValue,
                ["reviewCount"] = ratingCount,
                ["bestRating"] = 5,
                ["worstRating"] = 1,
            };
        }

        return JsonSerializer.Serialize(data, Options);
    }

    public static string BlogPosting(string title, string description, string url, string imageUrl, string siteName,
        string? authorName, DateTimeOffset? publishedAt)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = title,
            ["description"] = description,
            ["image"] = imageUrl,
            ["mainEntityOfPage"] = new Dictionary<string, object?> { ["@type"] = "WebPage", ["@id"] = url },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = string.IsNullOrWhiteSpace(authorName) ? "Organization" : "Person",
                ["name"] = string.IsNullOrWhiteSpace(authorName) ? siteName : authorName,
            },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = siteName,
            },
            ["datePublished"] = publishedAt?.ToString("O"),
            ["dateModified"] = publishedAt?.ToString("O"),
        };

        return JsonSerializer.Serialize(data, Options);
    }

    public static string FaqPage(IEnumerable<(string Question, string Answer)> items)
    {
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = items.Select(item => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = item.Question,
                ["acceptedAnswer"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Answer",
                    ["text"] = item.Answer,
                },
            }).ToList(),
        };

        return JsonSerializer.Serialize(data, Options);
    }

    public static string Breadcrumb(string siteUrl, string siteName, IEnumerable<(string Title, string? Url)> items)
    {
        var elements = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = 1,
                ["name"] = siteName,
                ["item"] = siteUrl,
            },
        };

        var position = 2;
        foreach (var item in items)
        {
            elements.Add(new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = position++,
                ["name"] = item.Title,
                ["item"] = string.IsNullOrWhiteSpace(item.Url) ? null : $"{siteUrl}{item.Url}",
            });
        }

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = elements,
        };

        return JsonSerializer.Serialize(data, Options);
    }
}
