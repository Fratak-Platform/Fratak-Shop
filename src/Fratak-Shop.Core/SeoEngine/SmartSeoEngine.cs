using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.SeoEngine;

/// <summary>
/// Entry point of «سئوی هوشمند»: runs the audit and builds the complete proposal
/// (every SEO field + the improved copy + internal links + FAQ + structured data)
/// in one call. The service layer persists the proposal as a reviewable record and
/// applies it only after the specialist confirms.
/// </summary>
public static class SmartSeoEngine
{
    public static SeoAuditResult Analyze(SeoAuditInput input)
    {
        var audit = SeoScorer.Audit(input);
        var keyword = string.IsNullOrWhiteSpace(input.FocusKeyword)
            ? SeoCopyWriter.GuessFocusKeyword(input)
            : input.FocusKeyword!.Trim();

        var existing = input.Existing;
        var proposal = new SeoProposal { FocusKeyword = keyword };
        var generated = new List<string>();

        void Track(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                generated.Add(field);
        }

        // ------------------------------------------------------- text fields
        proposal.Title = SeoCopyWriter.BuildTitle(input, keyword);
        Track("Title", string.IsNullOrWhiteSpace(existing?.Title) ? proposal.Title : null);

        proposal.MetaDescription = SeoCopyWriter.BuildMetaDescription(input, keyword);
        Track("MetaDescription", string.IsNullOrWhiteSpace(existing?.MetaDescription) ? proposal.MetaDescription : null);

        proposal.MetaKeywords = SeoCopyWriter.BuildMetaKeywords(keyword, input.SecondaryKeywords, input.Content);
        Track("MetaKeywords", string.IsNullOrWhiteSpace(existing?.MetaKeywords) ? proposal.MetaKeywords : null);

        proposal.BreadcrumbTitle = string.IsNullOrWhiteSpace(existing?.BreadcrumbTitle)
            ? SeoCopyWriter.BuildBreadcrumbTitle(input)
            : existing!.BreadcrumbTitle;
        Track("BreadcrumbTitle", string.IsNullOrWhiteSpace(existing?.BreadcrumbTitle) ? proposal.BreadcrumbTitle : null);

        proposal.Slug = SeoCopyWriter.BuildSlug(input, keyword);
        Track("Slug", null);

        proposal.ImageAlt = SeoCopyWriter.BuildImageAlt(input, keyword);
        Track("ImageAlt", string.IsNullOrWhiteSpace(input.ImageAlt) ? proposal.ImageAlt : null);

        // ------------------------------------------------------- social tags
        proposal.OgTitle = string.IsNullOrWhiteSpace(existing?.OgTitle) ? proposal.Title : existing!.OgTitle;
        proposal.OgDescription = string.IsNullOrWhiteSpace(existing?.OgDescription) ? proposal.MetaDescription : existing!.OgDescription;
        proposal.OgType = SeoCopyWriter.BuildOgType(input.EntityType);
        proposal.OgImageAlt = string.IsNullOrWhiteSpace(existing?.OgImageAlt) ? proposal.ImageAlt : existing!.OgImageAlt;
        proposal.XCard = string.IsNullOrWhiteSpace(existing?.XCard) ? SeoCopyWriter.BuildXCard(input) : existing!.XCard;
        proposal.XTitle = string.IsNullOrWhiteSpace(existing?.XTitle) ? proposal.Title : existing!.XTitle;
        proposal.XDescription = string.IsNullOrWhiteSpace(existing?.XDescription) ? proposal.MetaDescription : existing!.XDescription;

        foreach (var field in new[] { "OgTitle", "OgDescription", "OgImageAlt", "XCard", "XTitle", "XDescription" })
            generated.Add(field);

        // ------------------------------------------------------- canonical & sitemap
        var canonicalPath = BuildCanonicalPath(input);
        proposal.CanonicalUrl = string.IsNullOrWhiteSpace(existing?.CanonicalUrl)
            ? $"{input.SiteUrl.TrimEnd('/')}{canonicalPath}"
            : existing!.CanonicalUrl;
        Track("CanonicalUrl", string.IsNullOrWhiteSpace(existing?.CanonicalUrl) ? proposal.CanonicalUrl : null);

        proposal.IncludeInSitemap = existing?.IncludeInSitemap ?? true;
        proposal.SitemapPriority = existing?.SitemapPriority ?? DefaultPriority(input.EntityType);
        proposal.SitemapChangeFrequency = string.IsNullOrWhiteSpace(existing?.SitemapChangeFrequency)
            ? DefaultChangeFrequency(input.EntityType)
            : existing!.SitemapChangeFrequency;

        // ------------------------------------------------------- content & faq
        var faqs = SeoCopyWriter.BuildFaqSuggestions(input, keyword);
        proposal.FaqSuggestions = faqs;

        proposal.EnhancedShortDescription = ContentEnhancer.EnhanceShortDescription(input, keyword);
        Track("ShortDescription", string.IsNullOrWhiteSpace(input.ShortDescription) ? proposal.EnhancedShortDescription : null);

        proposal.EnhancedContent = input.EntityType switch
        {
            Data.Entities.Seo.SeoEntityType.Product => ContentEnhancer.EnhanceProductDescription(input, keyword, faqs),
            Data.Entities.Seo.SeoEntityType.BlogPost => ContentEnhancer.EnhanceBlogContent(input, keyword),
            _ => ContentEnhancer.RestructureExistingContent(input.Content, keyword),
        };

        proposal.InternalLinks = SeoCopyWriter.SuggestInternalLinks(input, keyword);
        proposal.KeywordIdeas = BuildKeywordIdeas(input, keyword);
        proposal.SecondaryKeywordsJson = System.Text.Json.JsonSerializer.Serialize(
            proposal.KeywordIdeas.Take(6).ToList(),
            new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        // ------------------------------------------------------- structured data
        proposal.SchemaType = input.EntityType switch
        {
            Data.Entities.Seo.SeoEntityType.Product => "Product",
            Data.Entities.Seo.SeoEntityType.BlogPost => "BlogPosting",
            _ => "WebPage",
        };
        proposal.StructuredDataJsonLd = SeoCopyWriter.BuildStructuredData(input, keyword, faqs, proposal.CanonicalUrl!, input.ImageAlt);

        proposal.GeneratedFields = generated.Distinct().ToList();
        proposal.Summary = audit.Summary;

        audit.Proposal = proposal;
        return audit;
    }

    /// <summary>Relative address of the content used for canonical and schema URLs.</summary>
    public static string BuildCanonicalPath(SeoAuditInput input) => input.EntityType switch
    {
        Data.Entities.Seo.SeoEntityType.Product => $"/product/{input.Slug}",
        Data.Entities.Seo.SeoEntityType.Category => $"/category/{input.Slug}",
        Data.Entities.Seo.SeoEntityType.BlogPost => $"/blog/{input.Slug}",
        Data.Entities.Seo.SeoEntityType.BlogCategory => $"/blog/category/{input.Slug}",
        _ => string.IsNullOrWhiteSpace(input.Slug) ? "/" : $"/page/{input.Slug}",
    };

    private static decimal DefaultPriority(Data.Entities.Seo.SeoEntityType entityType) => entityType switch
    {
        Data.Entities.Seo.SeoEntityType.Product => 0.8m,
        Data.Entities.Seo.SeoEntityType.Category => 0.7m,
        Data.Entities.Seo.SeoEntityType.BlogPost => 0.6m,
        _ => 0.5m,
    };

    private static string DefaultChangeFrequency(Data.Entities.Seo.SeoEntityType entityType) => entityType switch
    {
        Data.Entities.Seo.SeoEntityType.Product => "weekly",
        Data.Entities.Seo.SeoEntityType.BlogPost => "monthly",
        _ => "monthly",
    };

    /// <summary>
    /// Long-tail keyword ideas built from the focus keyword, the category and the
    /// terms that already appear in the content (a free, offline keyword tool).
    /// </summary>
    private static List<string> BuildKeywordIdeas(SeoAuditInput input, string keyword)
    {
        var ideas = new List<string>();
        var seeds = new List<string> { keyword };
        if (!string.IsNullOrWhiteSpace(input.CategoryName))
            seeds.Add(input.CategoryName!);

        var modifiers = input.EntityType == Data.Entities.Seo.SeoEntityType.Product
            ? new[] { "خرید", "قیمت", "مشخصات", "بررسی", "نقد و بررسی", "بهترین", "ارزان", "اصل" }
            : new[] { "راهنمای", "آموزش", "نکات", "چیست", "چگونه", "مقایسه", "علت", "بهترین" };

        foreach (var seed in seeds.Where(seed => !string.IsNullOrWhiteSpace(seed)).Distinct())
        {
            foreach (var modifier in modifiers)
            {
                ideas.Add($"{modifier} {seed}".Trim());
                if (ideas.Count >= 24)
                    break;
            }
        }

        ideas.AddRange(TextAnalyzer.TopTerms($"{input.CategoryName} {input.Title}", take: 6));

        return ideas
            .Where(idea => !string.IsNullOrWhiteSpace(idea))
            .Distinct()
            .Take(20)
            .ToList();
    }
}
