using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Contents;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Brands;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Home page built from the <c>HomeSections</c> table: the admin decides which blocks
/// are shown, in which order, with which title / link / item count.
/// </summary>
public class IndexModel(
    ISiteContentService siteContent,
    IBannerService bannerService,
    ICategoryService categoryService,
    IProductService productService,
    IBrandService brandService,
    IPostService postService,
    IRecommendationService recommendations,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    public List<HomeBlock> Blocks { get; private set; } = [];

    /// <summary>Swiper is only loaded when a slider is actually on the page.</summary>
    public bool NeedsSlider => Blocks.Any(b => b.Section.Type is HomeSectionType.HeroSlider or HomeSectionType.Deals
        or HomeSectionType.BestSellers or HomeSectionType.Newest or HomeSectionType.MostViewed
        or HomeSectionType.CategoryProducts or HomeSectionType.Brands
        or HomeSectionType.RecommendedForYou or HomeSectionType.RecentlyViewed);

    public async Task OnGetAsync()
    {
        var sections = await siteContent.GetActiveHomeSectionsAsync(HttpContext.RequestAborted);
        List<BannerDto>? banners = null;

        // The services share one scoped DbContext, so blocks are loaded sequentially.
        foreach (var section in sections)
        {
            var take = Math.Clamp(section.ItemCount <= 0 ? 10 : section.ItemCount, 1, 30);
            var block = new HomeBlock(section);

            switch (section.Type)
            {
                case HomeSectionType.HeroSlider:
                case HomeSectionType.PromoBanners:
                case HomeSectionType.WideBanners:
                    banners ??= await bannerService.GetActiveBannersAsync();
                    var position = section.Type switch
                    {
                        HomeSectionType.HeroSlider => BannerPosition.HeroSlider,
                        HomeSectionType.PromoBanners => BannerPosition.PromoCard,
                        _ => BannerPosition.WideBanner,
                    };
                    block.Banners = banners.Where(b => b.Position == position).Take(take).ToList();
                    break;

                case HomeSectionType.Categories:
                    block.Categories = (await categoryService.GetMenuCategoriesAsync()).Take(take).ToList();
                    break;

                case HomeSectionType.Deals:
                    block.Products = await LoadProductsAsync(ProductSortBy.MostDiscounted, take, onlyDiscounted: true, onlyAvailable: true);
                    break;

                case HomeSectionType.BestSellers:
                    block.Products = await LoadProductsAsync(ProductSortBy.BestSelling, take);
                    break;

                case HomeSectionType.Newest:
                    block.Products = await LoadProductsAsync(ProductSortBy.Newest, take);
                    break;

                case HomeSectionType.MostViewed:
                    block.Products = await LoadProductsAsync(ProductSortBy.MostViewed, take);
                    break;

                case HomeSectionType.CategoryProducts when section.CategoryId is not null:
                    block.Products = await LoadProductsAsync(ProductSortBy.Newest, take, categoryId: section.CategoryId);
                    break;

                case HomeSectionType.Features:
                    block.Features = site.Features.Take(take).ToList();
                    break;

                case HomeSectionType.Brands:
                    block.Brands = (await brandService.GetActiveBrandsAsync())
                        .Where(b => !string.IsNullOrWhiteSpace(b.LogoName))
                        .Take(take)
                        .ToList();
                    break;

                case HomeSectionType.LatestPosts:
                    block.Posts = await postService.GetLatestPostsAsync(take);
                    break;

                // Personal blocks: only shown when something is known about the visitor.
                case HomeSectionType.RecommendedForYou when site.Settings.IsRecommendationEnabled:
                    var viewed = RecentlyViewed.Read(Request);
                    if (CurrentUserId.HasValue || viewed.Count > 0)
                        block.Products = await recommendations.GetPersonalizedAsync(CurrentUserId, viewed, viewed, take);
                    break;

                case HomeSectionType.RecentlyViewed:
                    var recent = RecentlyViewed.Read(Request);
                    if (recent.Count > 0)
                        block.Products = await productService.GetCardsByIdsAsync(recent.Take(take));
                    break;
            }

            if (!block.IsEmpty)
                Blocks.Add(block);
        }

        var settings = site.Settings;
        await pageMeta.ApplyAsync(ViewData, SitePage.Home, settings.Tagline ?? "فروشگاه اینترنتی",
            settings.SiteDescription ?? $"{settings.SiteName}؛ خرید آنلاین با بهترین قیمت، ارسال سریع و ضمانت اصالت کالا.");
        AddStructuredData();
    }

    /// <summary>Organization + WebSite (sitelinks search box) structured data.</summary>
    private void AddStructuredData()
    {
        var settings = site.Settings;
        var home = pageMeta.AbsoluteUrl("/")!;

        var sameAs = new[] { settings.InstagramUrl, settings.TelegramUrl, settings.LinkedinUrl, settings.TwitterUrl, settings.YoutubeUrl, settings.AparatUrl }
            .Where(UrlSafety.IsHttpUrl)
            .ToList();

        var organization = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "OnlineStore",
            ["name"] = settings.SiteName,
            ["url"] = home,
            ["logo"] = pageMeta.AbsoluteUrl(site.LogoUrl)!,
        };
        if (!string.IsNullOrWhiteSpace(settings.OrganizationLegalName))
            organization["legalName"] = settings.OrganizationLegalName;
        if (!string.IsNullOrWhiteSpace(settings.SiteDescription))
            organization["description"] = settings.SiteDescription;
        if (sameAs.Count > 0)
            organization["sameAs"] = sameAs;
        if (!string.IsNullOrWhiteSpace(settings.SupportPhone) || !string.IsNullOrWhiteSpace(settings.SupportEmail))
        {
            var contact = new Dictionary<string, object> { ["@type"] = "ContactPoint", ["contactType"] = "customer service", ["areaServed"] = "IR", ["availableLanguage"] = "fa" };
            if (!string.IsNullOrWhiteSpace(settings.SupportPhone))
                contact["telephone"] = settings.SupportPhone;
            if (!string.IsNullOrWhiteSpace(settings.SupportEmail))
                contact["email"] = settings.SupportEmail;
            organization["contactPoint"] = contact;
        }
        if (!string.IsNullOrWhiteSpace(settings.Address))
        {
            var address = new Dictionary<string, object> { ["@type"] = "PostalAddress", ["streetAddress"] = settings.Address, ["addressCountry"] = "IR" };
            if (!string.IsNullOrWhiteSpace(settings.PostalCode))
                address["postalCode"] = settings.PostalCode;
            organization["address"] = address;
        }

        pageMeta.AddJsonLd(ViewData, organization);
        pageMeta.AddJsonLd(ViewData, new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = settings.SiteName,
            ["url"] = home,
            ["inLanguage"] = "fa-IR",
            ["potentialAction"] = new Dictionary<string, object>
            {
                ["@type"] = "SearchAction",
                ["target"] = new Dictionary<string, object>
                {
                    ["@type"] = "EntryPoint",
                    ["urlTemplate"] = pageMeta.AbsoluteUrl("/search") + "?q={search_term_string}",
                },
                ["query-input"] = "required name=search_term_string",
            },
        });
    }

    private async Task<List<ProductFilterData>> LoadProductsAsync(ProductSortBy sortBy, int take,
        bool onlyDiscounted = false, bool onlyAvailable = false, Guid? categoryId = null)
    {
        var result = await productService.GetProductByFilterAsync(new ProductFilterParams
        {
            OnlyActive = true,
            OnlyDiscounted = onlyDiscounted ? true : null,
            OnlyAvailable = onlyAvailable ? true : null,
            CategoryId = categoryId,
            SortBy = sortBy,
            Take = take,
        });

        return result.Data;
    }
}

public class HomeBlock(HomeSectionDto section)
{
    public HomeSectionDto Section { get; } = section;
    public List<BannerDto> Banners { get; set; } = [];
    public List<CategoryDto> Categories { get; set; } = [];
    public List<ProductFilterData> Products { get; set; } = [];
    public List<SiteFeatureDto> Features { get; set; } = [];
    public List<BrandDto> Brands { get; set; } = [];
    public List<BlogPostFilterData> Posts { get; set; } = [];

    public bool IsEmpty => Banners.Count == 0 && Categories.Count == 0 && Products.Count == 0
                           && Features.Count == 0 && Brands.Count == 0 && Posts.Count == 0;

    /// <summary>Unique DOM id (several product sliders may be on the page).</summary>
    public string DomId => $"home-{Section.Type.ToString().ToLowerInvariant()}-{Section.Id.ToString("N")[..8]}";
}
