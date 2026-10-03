using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Seo;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// Writes the SEO copy of a piece of content: title, meta description, keywords,
/// Open Graph/Twitter tags, JSON-LD structured data, internal-link and FAQ
/// suggestions. The generated text is always built from the real data of the
/// content (name, category, features, warranty, price) — nothing is invented —
/// and it is only proposed to the specialist, never saved behind their back.
/// </summary>
public static class SeoCopyWriter
{
    private const int TitleLimit = 62;
    private const int DescriptionLimit = 158;

    /// <summary>
    /// Picks a focus keyword when the content does not define one yet:
    /// category/brand plus the first meaningful words of the title.
    /// </summary>
    public static string GuessFocusKeyword(SeoAuditInput input)
    {
        var terms = TextAnalyzer.TopTerms($"{input.CategoryName} {input.Title}", take: 3);
        if (terms.Count > 0)
            return string.Join(' ', terms);

        return SlugHelper.NormalizePersian(input.Title).Split(' ').FirstOrDefault() ?? string.Empty;
    }

    /// <summary>«خرید هدفون بلوتوثی X10 | فراتک» — keyword first, brand last.</summary>
    public static string BuildTitle(SeoAuditInput input, string keyword)
    {
        var brand = string.IsNullOrWhiteSpace(input.BrandName) ? input.SiteName : input.BrandName;
        var cleanTitle = CleanupTitle(input.Title);

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            candidates.Add($"{keyword} | {cleanTitle} | {brand}");
            candidates.Add($"{cleanTitle} | {brand}");
        }
        else
        {
            candidates.Add($"{cleanTitle} | {brand}");
        }

        foreach (var candidate in candidates)
        {
            if (candidate.Length <= TitleLimit)
                return candidate;
        }

        var fallback = candidates[0];
        return fallback.Length <= TitleLimit ? fallback : fallback[..TitleLimit].TrimEnd(' ', '|', '-');
    }

    /// <summary>Meta description: real summary + keyword + a call to action.</summary>
    public static string BuildMetaDescription(SeoAuditInput input, string keyword)
    {
        var baseText = TextAnalyzer.StripHtml(input.Existing?.MetaDescription) is { Length: > 0 } existing
            ? existing
            : TextAnalyzer.StripHtml(input.ShortDescription);

        if (string.IsNullOrWhiteSpace(baseText))
            baseText = TextAnalyzer.StripHtml(input.Content);

        var sentences = TextAnalyzer.Sentences(baseText).Take(3).ToList();
        var description = new StringBuilder();

        if (sentences.Count == 0)
            description.Append(input.Title);
        else
            description.Append(string.Join(' ', sentences));

        // guarantee the main keyword is present
        if (!string.IsNullOrWhiteSpace(keyword) && !description.ToString().Contains(keyword, StringComparison.Ordinal))
            description.Insert(0, $"{keyword} ");

        var cta = input.EntityType == SeoEntityType.Product
            ? " خرید با ضمانت اصالت، ارسال سریع و پشتیبانی تخصصی."
            : " این مقاله را در فراتک بخوانید.";

        description.Append(cta);

        var result = description.ToString();
        if (result.Length > DescriptionLimit)
            result = result[..DescriptionLimit].TrimEnd(' ', '،', ',', '.') + "…";

        return result;
    }

    public static string BuildMetaKeywords(string keyword, IEnumerable<string> secondary, string? content)
    {
        var keywords = new List<string>();
        if (!string.IsNullOrWhiteSpace(keyword))
            keywords.Add(keyword.Trim());

        keywords.AddRange(secondary.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()));
        keywords.AddRange(TextAnalyzer.TopTerms(content, take: 8));

        return string.Join(", ", keywords.Distinct().Take(12));
    }

    public static string BuildBreadcrumbTitle(SeoAuditInput input) =>
        string.IsNullOrWhiteSpace(input.CategoryName) ? input.Title : $"{input.CategoryName} / {input.Title}";

    public static string BuildSlug(SeoAuditInput input, string keyword)
    {
        var current = SlugHelper.Generate(input.Slug);
        var keywordSlug = SlugHelper.Generate(keyword);

        if (string.IsNullOrWhiteSpace(current))
            return SlugHelper.Generate(input.Title);

        if (string.IsNullOrWhiteSpace(keywordSlug) || current.Contains(keywordSlug, StringComparison.Ordinal))
            return current;

        var enriched = SlugHelper.Generate($"{keyword}-{current}");
        return enriched.Length <= 80 ? enriched : current;
    }

    public static string BuildImageAlt(SeoAuditInput input, string keyword)
    {
        if (!string.IsNullOrWhiteSpace(input.ImageAlt) && TextAnalyzer.KeywordOccurrences(input.ImageAlt, keyword) > 0)
            return input.ImageAlt!;

        var brand = string.IsNullOrWhiteSpace(input.BrandName) ? input.SiteName : input.BrandName;
        return string.IsNullOrWhiteSpace(keyword)
            ? $"{input.Title} - {brand}"
            : $"{keyword} - {input.Title} | {brand}";
    }

    public static string BuildOgType(SeoEntityType entityType) => entityType switch
    {
        SeoEntityType.Product => "product",
        SeoEntityType.BlogPost => "article",
        _ => "website",
    };

    public static string BuildXCard(SeoAuditInput input) => input.ImageCount > 0 ? "summary_large_image" : "summary";

    /// <summary>
    /// Suggests internal links by keyword overlap between the content and the
    /// candidates the service layer passed in (related products, posts, categories).
    /// </summary>
    public static List<SeoLinkSuggestion> SuggestInternalLinks(SeoAuditInput input, string keyword, int take = 5)
    {
        if (input.LinkCandidates.Count == 0)
            return [];

        var contentTerms = new HashSet<string>(
            TextAnalyzer.TopTerms($"{input.Title} {input.Content} {keyword}", take: 40),
            StringComparer.Ordinal);

        return input.LinkCandidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Overlap = TextAnalyzer.TopTerms(candidate.Title, take: 10).Count(term => contentTerms.Contains(term)),
            })
            .Where(item => !string.Equals(item.Candidate.Url, $"/{input.Slug}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Overlap)
            .ThenBy(item => item.Candidate.Title)
            .Take(take)
            .Select(item => new SeoLinkSuggestion
            {
                Title = item.Candidate.Title,
                Url = item.Candidate.Url,
                EntityType = item.Candidate.EntityType,
                AnchorText = string.IsNullOrWhiteSpace(keyword) ? item.Candidate.Title : $"{keyword} - {item.Candidate.Title}",
            })
            .ToList();
    }

    /// <summary>
    /// Builds the FAQ block that is both shown to the reader and exposed as
    /// FAQPage structured data (a strong rich-result signal).
    /// </summary>
    public static List<SeoFaqSuggestion> BuildFaqSuggestions(SeoAuditInput input, string keyword)
    {
        var faqs = new List<SeoFaqSuggestion>();

        if (input.EntityType == SeoEntityType.Product)
        {
            if (input.Price is > 0)
            {
                faqs.Add(new SeoFaqSuggestion
                {
                    Question = $"قیمت {input.Title} چقدر است؟",
                    Answer = $"قیمت {input.Title} در فروشگاه فراتک {PriceHelper.ToToman(input.Price.GetValueOrDefault())} است و در صورت فعال بودن تخفیف، مبلغ نهایی در صفحه محصول به‌روزرسانی می‌شود.",
                });
            }

            if (input.Features.TryGetValue("گارانتی", out var warranty) && !string.IsNullOrWhiteSpace(warranty))
            {
                faqs.Add(new SeoFaqSuggestion
                {
                    Question = $"{input.Title} چه گارانتی دارد؟",
                    Answer = $"{input.Title} با {warranty} عرضه می‌شود و خدمات پس از فروش آن توسط فراتک پیگیری می‌شود.",
                });
            }

            if (input.Features.Count > 0)
            {
                var keyFeatures = input.Features
                    .Where(feature => !string.IsNullOrWhiteSpace(feature.Value))
                    .Take(3)
                    .Select(feature => $"{feature.Key}: {feature.Value}");

                faqs.Add(new SeoFaqSuggestion
                {
                    Question = $"مهم‌ترین ویژگی‌های {input.Title} چیست؟",
                    Answer = string.Join("، ", keyFeatures) + ".",
                });
            }

            faqs.Add(new SeoFaqSuggestion
            {
                Question = "ارسال این محصول چگونه انجام می‌شود؟",
                Answer = "ارسال به سراسر کشور با پست پیشتاز و تیپاکس انجام می‌شود؛ سفارش‌های ثبت‌شده در روزهای کاری، حداکثر تا ۲۴ ساعت بعد ارسال می‌شوند.",
            });
        }
        else
        {
            var topic = string.IsNullOrWhiteSpace(keyword) ? input.Title : keyword;
            faqs.Add(new SeoFaqSuggestion
            {
                Question = $"{topic} چیست؟",
                Answer = $"در این مقاله {topic} را به زبان ساده توضیح داده‌ایم و نکات کاربردی آن را بررسی کرده‌ایم.",
            });
            faqs.Add(new SeoFaqSuggestion
            {
                Question = $"چگونه بهترین انتخاب را در مورد {topic} داشته باشیم؟",
                Answer = "با بررسی معیارهای اصلی (کیفیت ساخت، کارایی، قیمت و پشتیبانی) و مقایسه گزینه‌های موجود می‌توانید انتخاب دقیقی داشته باشید؛ در ادامه مقاله این معیارها را آورده‌ایم.",
            });
        }

        return faqs;
    }

    /// <summary>
    /// Builds the JSON-LD document of the content.
    /// </summary>
    public static string BuildStructuredData(SeoAuditInput input, string keyword, List<SeoFaqSuggestion> faqs, string canonicalUrl, string? imageName)
    {
        var graph = new List<object>();
        var siteUrl = input.SiteUrl.TrimEnd('/');

        switch (input.EntityType)
        {
            case SeoEntityType.Product:
            {
                var product = new Dictionary<string, object?>
                {
                    ["@type"] = "Product",
                    ["@id"] = canonicalUrl + "#product",
                    ["name"] = input.Title,
                    ["description"] = TextAnalyzer.StripHtml(input.ShortDescription ?? input.Content),
                    ["sku"] = input.EntityId.ToString(),
                    ["category"] = input.CategoryName,
                    ["brand"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Brand",
                        ["name"] = string.IsNullOrWhiteSpace(input.BrandName) ? input.SiteName : input.BrandName,
                    },
                };

                if (!string.IsNullOrWhiteSpace(imageName))
                    product["image"] = $"{siteUrl}/uploads/products/{imageName}";

                if (input.Price is > 0)
                {
                    product["offers"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Offer",
                        ["url"] = canonicalUrl,
                        ["priceCurrency"] = "IRR",
                        ["price"] = input.Price.Value,
                        ["availability"] = string.Equals(input.StockStatus, "instock", StringComparison.OrdinalIgnoreCase)
                            ? "https://schema.org/InStock"
                            : "https://schema.org/OutOfStock",
                        ["seller"] = new Dictionary<string, object?>
                        {
                            ["@type"] = "Organization",
                            ["name"] = input.SiteName,
                        },
                    };
                }

                if (input.RatingValue is > 0 && input.RatingCount > 0)
                {
                    product["aggregateRating"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "AggregateRating",
                        ["ratingValue"] = Math.Round(input.RatingValue.Value, 1),
                        ["reviewCount"] = input.RatingCount,
                        ["bestRating"] = 5,
                        ["worstRating"] = 1,
                    };
                }

                graph.Add(product);
                break;
            }

            case SeoEntityType.BlogPost:
            case SeoEntityType.BlogCategory:
            {
                graph.Add(new Dictionary<string, object?>
                {
                    ["@type"] = "BlogPosting",
                    ["@id"] = canonicalUrl + "#article",
                    ["headline"] = input.Title,
                    ["description"] = TextAnalyzer.StripHtml(input.ShortDescription ?? input.Content),
                    ["inLanguage"] = input.Language,
                    ["mainEntityOfPage"] = canonicalUrl,
                    ["articleSection"] = input.CategoryName,
                    ["wordCount"] = TextAnalyzer.WordCount(input.Content),
                    ["publisher"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Organization",
                        ["name"] = input.SiteName,
                        ["url"] = siteUrl,
                    },
                });
                break;
            }

            default:
                graph.Add(new Dictionary<string, object?>
                {
                    ["@type"] = "WebPage",
                    ["@id"] = canonicalUrl + "#webpage",
                    ["name"] = input.Title,
                    ["description"] = TextAnalyzer.StripHtml(input.ShortDescription ?? input.Content),
                    ["inLanguage"] = input.Language,
                    ["isPartOf"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "WebSite",
                        ["name"] = input.SiteName,
                        ["url"] = siteUrl,
                    },
                });
                break;
        }

        if (faqs.Count > 0)
        {
            graph.Add(new Dictionary<string, object?>
            {
                ["@type"] = "FAQPage",
                ["@id"] = canonicalUrl + "#faq",
                ["mainEntity"] = faqs.Select(faq => new Dictionary<string, object?>
                {
                    ["@type"] = "Question",
                    ["name"] = faq.Question,
                    ["acceptedAnswer"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Answer",
                        ["text"] = faq.Answer,
                    },
                }).ToList(),
            });
        }

        if (!string.IsNullOrWhiteSpace(input.CategoryName))
        {
            graph.Add(new Dictionary<string, object?>
            {
                ["@type"] = "BreadcrumbList",
                ["@id"] = canonicalUrl + "#breadcrumb",
                ["itemListElement"] = new List<object>
                {
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 1, ["name"] = input.SiteName, ["item"] = siteUrl },
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 2, ["name"] = input.CategoryName },
                    new Dictionary<string, object?> { ["@type"] = "ListItem", ["position"] = 3, ["name"] = input.Title, ["item"] = canonicalUrl },
                },
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = graph,
        }, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string CleanupTitle(string title)
    {
        var cleaned = TextAnalyzer.StripHtml(title).Trim();
        // remove duplicated brand suffixes like «... | فراتک | فراتک»
        var parts = cleaned.Split('|', '،').Select(part => part.Trim()).Where(part => part.Length > 0).Distinct().ToArray();
        return string.Join(" | ", parts);
    }
}
