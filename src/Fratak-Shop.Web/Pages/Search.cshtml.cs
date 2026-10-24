using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Brands;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// Product listing: search results, category pages (/category/{slug}),
/// brand pages (/brand/{slug}) and the special lists (discounts, best sellers).
/// </summary>
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class SearchModel(
    IProductService productService,
    ICategoryService categoryService,
    IBrandService brandService,
    IProductFilterService filterService,
    Fratak_Shop.Core.Services.Searches.ISearchInsightsService insights,
    IPageMetaService pageMeta) : ShopPageModel
{
    /// <summary>Spelling suggestion when a text search found nothing.</summary>
    public string? DidYouMean { get; private set; }

    /// <summary>Popular searches offered on an empty result.</summary>
    public IReadOnlyList<string> PopularSearches { get; private set; } = [];

    public const int PageSize = 16;

    public static readonly (ProductSortBy Value, string Title)[] SortOptions =
    [
        (ProductSortBy.Newest, "جدیدترین"),
        (ProductSortBy.BestSelling, "پرفروش ترین"),
        (ProductSortBy.MostViewed, "پربازدیدترین"),
        (ProductSortBy.Cheapest, "ارزان ترین"),
        (ProductSortBy.MostExpensive, "گران ترین"),
        (ProductSortBy.MostDiscounted, "بیشترین تخفیف"),
    ];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "category")]
    public string? CategorySlug { get; set; }

    [BindProperty(SupportsGet = true, Name = "brand")]
    public List<string> BrandSlugs { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public decimal? MinPrice { get; set; }

    [BindProperty(SupportsGet = true)]
    public decimal? MaxPrice { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Available { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Discounted { get; set; }

    [BindProperty(SupportsGet = true)]
    public ProductSortBy Sort { get; set; } = ProductSortBy.Newest;

    // FromQuery: "page" is also a Razor Pages route value (the page path), which would win over the query string.
    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    public ProductFilterResult Result { get; private set; } = null!;
    public List<CategoryDto> Categories { get; private set; } = [];
    /// <summary>Brand filter options of the current listing (with product counts).</summary>
    public List<BrandFacetDto> BrandFacets { get; private set; } = [];

    /// <summary>/brand/{slug}: the brand is fixed by the URL, so no brand filter box.</summary>
    public bool IsBrandPage { get; private set; }

    /// <summary>
    /// Brand and specification filters are category specific: without a category only the
    /// general filters (and the specs marked "ShowWithoutCategory") are offered.
    /// </summary>
    public bool ShowBrandFilter => !IsBrandPage && BrandFacets.Count > 0;
    public CategoryDto? Category { get; private set; }
    public BrandDto? SingleBrand { get; private set; }
    public PagerModel Pager { get; private set; } = null!;
    public string Heading { get; private set; } = "همه محصولات";

    /// <summary>Specification filter boxes with value counts (query keys "f-{slug}").</summary>
    public List<SpecificationFacetDto> Facets { get; private set; } = [];

    /// <summary>Selected specification values by filter slug.</summary>
    public Dictionary<string, List<string>> SelectedSpecifications { get; private set; } = [];

    public const string SpecificationPrefix = "f-";

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Query) || !string.IsNullOrWhiteSpace(CategorySlug) || BrandSlugs.Count > 0
        || MinPrice.HasValue || MaxPrice.HasValue || Available || Discounted || SelectedSpecifications.Count > 0;

    public async Task<IActionResult> OnGetAsync()
    {
        BrandSlugs = BrandSlugs.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().Take(20).ToList();
        IsBrandPage = Request.Path.StartsWithSegments("/brand", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(CategorySlug))
        {
            Category = await categoryService.GetCategoryBySlugAsync(CategorySlug);
            if (Category is null || !Category.IsActive)
                return NotFound();
        }

        if (BrandSlugs.Count == 1)
            SingleBrand = await brandService.GetBrandBySlugAsync(BrandSlugs[0]);

        if (MinPrice.HasValue && MaxPrice.HasValue && MinPrice > MaxPrice)
            (MinPrice, MaxPrice) = (MaxPrice, MinPrice);

        // Only the filters configured for this listing are accepted (no arbitrary feature keys).
        var attributes = await filterService.GetAttributesAsync(CategorySlug);
        var specifications = new Dictionary<string, List<string>>();
        foreach (var attribute in attributes)
        {
            var values = Request.Query[SpecificationPrefix + attribute.Slug]
                .Where(v => !string.IsNullOrWhiteSpace(v) && v!.Length <= 200)
                .Select(v => v!.Trim())
                .Distinct()
                .Take(10)
                .ToList();

            if (values.Count == 0)
                continue;

            specifications[attribute.FeatureKey] = values;
            SelectedSpecifications[attribute.Slug] = values;
        }

        var filter = new ProductFilterParams
        {
            Search = Query,
            CategorySlug = CategorySlug,
            BrandSlugs = BrandSlugs,
            MinPrice = MinPrice,
            MaxPrice = MaxPrice,
            OnlyAvailable = Available ? true : null,
            OnlyDiscounted = Discounted ? true : null,
            OnlyActive = true,
            SortBy = Sort,
            PageId = PageNumber,
            Take = PageSize,
            Specifications = specifications,
        };

        var hasQuery = !string.IsNullOrWhiteSpace(Query);
        if (hasQuery)
        {
            Query = Query!.Trim();
            if (Query.Length > 100)
                Query = Query[..100];
            filter.Search = Query;
            filter.SearchTermGroups = await insights.ExpandAsync(Query); // synonyms
        }

        Result = await productService.GetProductByFilterAsync(filter);

        if (hasQuery && PageNumber <= 1)
        {
            var visitor = CurrentUserId?.ToString("N") ?? HttpContext.Connection.RemoteIpAddress?.ToString();
            await insights.LogAsync(Query, Result.EntityCount, visitor);
        }

        if (Result.Data.Count == 0)
        {
            if (hasQuery)
                DidYouMean = await insights.DidYouMeanAsync(Query);
            PopularSearches = await insights.GetPopularAsync();
        }
        if (attributes.Count > 0)
            Facets = await productService.GetSpecificationFacetsAsync(filter, attributes);

        if (!IsBrandPage)
        {
            if (Category is not null)
                BrandFacets = await productService.GetBrandFacetsAsync(filter);
            else if (BrandSlugs.Count > 0)
                // No category: only the already selected brands stay visible, so they can be removed.
                BrandFacets = (await productService.GetBrandFacetsAsync(filter)).Where(b => b.IsSelected).ToList();
        }

        Categories = await categoryService.GetMenuCategoriesAsync();
        Pager = PagerModel.From(Result, Request);

        await ApplySeoAsync();

        return Page();
    }

    private async Task ApplySeoAsync()
    {
        if (Category is not null)
        {
            Heading = Category.Name;
            pageMeta.Apply(ViewData, Category.Seo, $"خرید {Category.Name}", Category.Description,
                Helpers.ImageHelper.Category(Category.ImageName));
            ViewData[PageMetaKeys.SitePage] = SitePage.CategoryDetails;
            ViewData[PageMetaKeys.AlertCategoryId] = Category.Id;
            pageMeta.AddBreadcrumbs(ViewData, ("محصولات", "/products"), (Category.Name, $"/category/{Category.Slug}"));
            AddItemList();
        }
        else if (SingleBrand is not null)
        {
            Heading = $"محصولات {SingleBrand.Name}";
            pageMeta.Apply(ViewData, SingleBrand.Seo, $"محصولات {SingleBrand.Name}", SingleBrand.Description,
                Helpers.ImageHelper.Brand(SingleBrand.LogoName));
            ViewData[PageMetaKeys.SitePage] = SitePage.Products;
            if (IsBrandPage)
            {
                pageMeta.AddBreadcrumbs(ViewData, ("برندها", null), (SingleBrand.Name, $"/brand/{SingleBrand.Slug}"));
                AddItemList();
            }
        }
        else if (!string.IsNullOrWhiteSpace(Query))
        {
            Heading = $"نتایج جستجو برای «{Query}»";
            await pageMeta.ApplyAsync(ViewData, SitePage.Search, Heading);
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
        }
        else
        {
            Heading = Discounted ? "شگفت انگیزها" : Sort == ProductSortBy.BestSelling ? "پرفروش ترین ها" : "همه محصولات";
            await pageMeta.ApplyAsync(ViewData, SitePage.Products, Heading, "لیست محصولات فروشگاه با امکان فیلتر بر اساس دسته بندی، برند و قیمت.");
            pageMeta.AddBreadcrumbs(ViewData, (Heading, null));
        }

        // Filtered / paginated variations should not compete with the main listing.
        if (PageNumber > 1 || BrandSlugs.Count > 1 || MinPrice.HasValue || MaxPrice.HasValue || SelectedSpecifications.Count > 0)
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
    }

    /// <summary>ItemList JSON-LD of the products shown on a category / brand page (first page only).</summary>
    private void AddItemList()
    {
        if (PageNumber > 1 || Result.Data.Count == 0)
            return;

        pageMeta.AddJsonLd(ViewData, new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ItemList",
            ["name"] = Heading,
            ["numberOfItems"] = Result.EntityCount,
            ["itemListElement"] = Result.Data.Select((p, index) => new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["url"] = pageMeta.AbsoluteUrl($"/product/{p.Slug}"),
                ["name"] = p.Name,
            }).ToList(),
        });
    }
}
