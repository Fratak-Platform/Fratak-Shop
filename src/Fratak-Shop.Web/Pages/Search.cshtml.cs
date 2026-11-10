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
    IPageMetaService pageMeta,
    Fratak_Shop.Core.Services.Seo.Autopilot.ISeoLandingService landings,
    Fratak_Shop.Core.Services.Smart.IBuyerGuideService buyerGuide,
    Fratak_Shop.Core.Services.Marketing.IBannerService banners,
    Fratak_Shop.Web.Infrastructure.ISiteContext site) : ShopPageModel
{
    /// <summary>دور ۲۳: سربرگ صفحهٔ دسته (۱۹۲۰×۳۰۰ + نسخهٔ موبایل) یا null.</summary>
    public Fratak_Shop.Core.DTOs.Banners.Query.BannerDto? HeaderBanner { get; private set; }

    /// <summary>دور ۲۳: معرفی کوتاه بالای صفحهٔ دسته (پاراگراف اول توضیح، متن ساده) یا null.</summary>
    public string? IntroText { get; private set; }

    /// <summary>دور ۲۳: پرسش‌های متداول پایین صفحهٔ دسته (از دادهٔ واقعی).</summary>
    public List<Fratak_Shop.Core.Services.Categories.CategoryQuestion> CategoryQuestions { get; private set; } = [];

    /// <summary>دور ۲۳: دستهٔ کم‌محتوا (کمتر از ۳ کالا) ← noindex.</summary>
    public bool IsThinCategory { get; private set; }

    /// <summary>دور ۱۹ (C8): «کمکم کن انتخاب کنم» در صفحهٔ دسته – فقط با ?quiz=1 اجرا می‌شود (هزینهٔ پرس‌وجو برای بقیه صفر).</summary>
    /// <para>نکته: مقدار به‌صورت متن خوانده می‌شود چون اتصال bool در ASP.NET فقط «true/false» را می‌پذیرد و «quiz=1» را نادیده می‌گیرد.</para>
    [FromQuery(Name = "quiz")]
    public string? QuizParam { get; set; }

    /// <summary>پرسش‌نامه باز است؟ مثال: ?quiz=1 یا ?quiz=true ← بله؛ بدون پارامتر یا ?quiz=0 ← خیر.</summary>
    public bool QuizOpen => QuizParam is "1" || string.Equals(QuizParam, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>سقف بودجهٔ انتخاب‌شده در پرسشنامه (تومان).</summary>
    [FromQuery(Name = "budget")]
    public decimal? QuizBudget { get; set; }

    /// <summary>اولویت انتخاب‌شده: popular، cheap یا نام یک ویژگی عددی دسته.</summary>
    [FromQuery(Name = "priority")]
    public string? QuizPriority { get; set; }

    public Fratak_Shop.Core.Services.Smart.QuizResult? Quiz { get; private set; }

    public List<ProductFilterData> QuizPicks { get; private set; } = [];

    /// <summary>
    /// لینک‌های «خرید {دسته} زیر X تومان» از قیمت واقعی محصولات موجود دسته (فقط صفحهٔ اول دستهٔ بدون فیلتر).
    /// </summary>
    public IReadOnlyList<Fratak_Shop.Core.Services.Seo.Autopilot.SeoLandingLink> PriceLandings { get; private set; } = [];

    /// <summary>Spelling suggestion when a text search found nothing.</summary>
    public string? DidYouMean { get; private set; }

    /// <summary>دور ۱۹ (C15): عبارت اصلی کاربر وقتی با اصلاح چیدمان کیبورد جست‌وجو شد. مثال: «ihvni» (و Query = «هدفون»).</summary>
    public string? CorrectedFrom { get; private set; }

    /// <summary>دور ۱۹ (C16): پرفروش‌های موجود برای وقتی که جست‌وجو نتیجه ندارد.</summary>
    public List<ProductFilterData> Suggestions { get; private set; } = [];

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

    /// <summary>دور ۲۱: دستهٔ متناظر با جست‌وجوی کلی (مثلاً «گوشی» ← «گوشی موبایل») برای کارت بالای نتیجه‌ها؛ null یعنی جست‌وجوی دقیق.</summary>
    public (string Name, string Slug)? Spotlight { get; private set; }
    /// <summary>Brand filter options of the current listing (with product counts).</summary>
    public List<BrandFacetDto> BrandFacets { get; private set; } = [];

    /// <summary>/brand/{slug}: the brand is fixed by the URL, so no brand filter box.</summary>
    public bool IsBrandPage { get; private set; }

    /// <summary>
    /// متن معرفی دسته/برند زیر فهرست کالاها (فقط صفحهٔ اول). متن HTML (مثلاً نوشتهٔ موتور سئو) پاک‌سازی می‌شود
    /// و متن سادهٔ قدیمی به پاراگراف تبدیل می‌شود. این متن برای سئوی صفحه‌های دسته و برند لازم است.
    /// </summary>
    public string? ListingDescriptionHtml { get; private set; }

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
            // دور ۲۳: دستهٔ سیستمی («دسته‌بندی نشده») صفحهٔ عمومی ندارد.
            if (Category is null || !Category.IsActive || Category.IsSystem)
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

        // دور ۱۹ (C15): «ihvni» با کیبورد انگلیسی تایپ شده ولی منظور «هدفون» بوده ← یک بار با چیدمان درست جست‌وجو می‌شود
        // و نتیجه با پیام شفاف («نتایج «هدفون» نمایش داده شد») نشان داده می‌شود. فقط وقتی نتیجهٔ اصلی خالی است (یک پرس‌وجوی اضافه).
        if (Result.Data.Count == 0 && hasQuery && Fratak_Shop.Core.Services.Smart.KeyboardLayout.Swap(Query) is { } swapped
            && !string.Equals(swapped, Query, StringComparison.Ordinal))
        {
            var originalGroups = filter.SearchTermGroups;
            filter.Search = swapped;
            filter.SearchTermGroups = await insights.ExpandAsync(swapped);
            var corrected = await productService.GetProductByFilterAsync(filter);
            if (corrected.Data.Count > 0)
            {
                Result = corrected;
                CorrectedFrom = Query;
                Query = swapped;
            }
            else
            {
                // جست‌وجوی جایگزین هم چیزی نداشت ← فیلتر به حالت اصلی برمی‌گردد تا فیلترهای کناری با عبارت کاربر ساخته شوند.
                filter.Search = Query;
                filter.SearchTermGroups = originalGroups;
            }
        }

        if (Result.Data.Count == 0)
        {
            if (hasQuery)
                DidYouMean = await insights.DidYouMeanAsync(Query);
            PopularSearches = await insights.GetPopularAsync();
            // دور ۱۹ (C16): صفحهٔ خالی نه – پرفروش‌های موجود (همان دسته اگر انتخاب شده) به‌عنوان پیشنهاد.
            var fallback = await productService.GetProductByFilterAsync(new ProductFilterParams
            {
                CategorySlug = Category is null ? null : CategorySlug,
                OnlyAvailable = true,
                OnlyActive = true,
                SortBy = ProductSortBy.BestSelling,
                PageId = 1,
                Take = 8,
            });
            Suggestions = fallback.Data;
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

        if (QuizOpen && Category is not null)
        {
            var priority = QuizPriority is { Length: > 60 } ? null : QuizPriority;
            Quiz = await buyerGuide.QuizAsync(Category.Id, QuizBudget is > 0 ? QuizBudget : null, priority, HttpContext.RequestAborted);
            if ((QuizBudget is > 0 || !string.IsNullOrWhiteSpace(priority)) && Quiz.Picks.Count > 0)
            {
                var cards = await productService.GetCardsByIdsAsync(Quiz.Picks, onlyInStock: true);
                QuizPicks = Quiz.Picks.Select(id => cards.FirstOrDefault(c => c.Id == id)).OfType<ProductFilterData>().ToList();
            }
        }

        Categories = await categoryService.GetMenuCategoriesAsync();
        // دور ۲۱: عبارت کلی («گوشی»، «هدفون بی سیم») ← کارت دستهٔ متناظر بالای نتیجه‌ها؛ عبارت دقیق («گوشی A55») ← فقط کالاها.
        // از فهرست منوی کش‌شده محاسبه می‌شود (پرس‌وجوی اضافه ندارد).
        if (hasQuery && Category is null && !IsBrandPage && PageNumber <= 1)
        {
            var flat = Categories.Where(c => c.IsActive).Select(c => (c.Id, c.Name, c.Slug))
                .Concat(Categories.SelectMany(c => c.SubCategories).Where(c => c.IsActive).Select(c => (c.Id, c.Name, c.Slug)));
            if (Fratak_Shop.Core.Services.Smart.SearchIntent.MatchCategory(Query, flat) is { } spot)
                Spotlight = (spot.Name, spot.Slug);
        }
        Pager = PagerModel.From(Result, Request);

        await ApplySeoAsync();

        return Page();
    }

    /// <summary>توضیح دسته/برند به HTML امن. مثال: «خط اول\nخط دوم» → دو پاراگراف.</summary>
    private string? DescriptionHtml(string? description)
    {
        if (PageNumber > 1 || string.IsNullOrWhiteSpace(description))
            return null;
        if (description.Contains('<'))
            return Fratak_Shop.Core.Utilities.XssSecurity.SanitizeText(description);
        return string.Concat(description.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>"));
    }

    /// <summary>
    /// دور ۲۳ برای صفحهٔ دسته: سربرگ (بنر)، معرفی کوتاه بالا، پرسش‌های متداول پایین و noindex دستهٔ کم‌محتوا (فقط نمای بدون فیلتر سنجیده می‌شود).
    /// </summary>
    private async Task ApplyCategoryRound23Async(CategoryDto category)
    {
        var unfiltered = string.IsNullOrWhiteSpace(Query) && !Available && !Discounted && BrandSlugs.Count == 0
                         && !MinPrice.HasValue && !MaxPrice.HasValue && SelectedSpecifications.Count == 0;
        if (unfiltered && Fratak_Shop.Core.Services.Categories.CategoryService.IsThin(Result.EntityCount))
        {
            IsThinCategory = true;
            ViewData[PageMetaKeys.Robots] = "noindex, follow";
        }
        if (PageNumber > 1)
            return;
        HeaderBanner = await banners.GetCategoryHeaderAsync(category.Id);
        IntroText = Intro(category.Description);
        if (unfiltered && !IsThinCategory)
        {
            var facts = await categoryService.GetCategoryFactsAsync(category.Id);
            CategoryQuestions = Fratak_Shop.Core.Services.Categories.CategoryFaq.Build(category.Name, facts, site.Settings.SiteName);
        }
    }

    /// <summary>
    /// معرفی کوتاه: اولین جملهٔ کامل(ها)ی توضیح تا حدود ۲۲۰ نویسه، متن ساده. مثال: «هدفون بی‌سیم… . جملهٔ دوم…» ← فقط تا پایان جمله‌ای که از ۲۲۰ نگذرد.
    /// </summary>
    public static string? Intro(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;
        var text = Fratak_Shop.Core.Services.Seo.Engine.PersianText.HtmlToText(description);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length < 40)
            return null;
        if (text.Length <= 220)
            return text;
        var cut = text.LastIndexOfAny(['.', '!', '؟', '?'], 220);
        return cut >= 60 ? text[..(cut + 1)] : text[..220].TrimEnd() + "…";
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
            ListingDescriptionHtml = DescriptionHtml(Category.Description);
            await ApplyCategoryRound23Async(Category);
            if (PageNumber <= 1 && !MinPrice.HasValue && !MaxPrice.HasValue && BrandSlugs.Count == 0 && SelectedSpecifications.Count == 0)
                PriceLandings = await landings.PriceLandingsAsync(Category.Id, Category.Slug, Category.Name);
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
                ListingDescriptionHtml = DescriptionHtml(SingleBrand.Description);
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
