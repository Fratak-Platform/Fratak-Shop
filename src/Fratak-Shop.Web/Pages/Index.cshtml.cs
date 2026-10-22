using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Brands.Query;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Brands;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

public class IndexModel(
    IBannerService bannerService,
    ICategoryService categoryService,
    IProductService productService,
    IBrandService brandService,
    IPostService postService,
    IPageMetaService pageMeta,
    Microsoft.Extensions.Options.IOptions<ShopOptions> shopOptions) : ShopPageModel
{
    private const int SliderSize = 10;

    public List<BannerDto> HeroBanners { get; private set; } = [];
    public List<BannerDto> WideBanners { get; private set; } = [];
    public List<BannerDto> PromoBanners { get; private set; } = [];
    public List<CategoryDto> Categories { get; private set; } = [];
    public List<ProductFilterData> DealProducts { get; private set; } = [];
    public List<ProductFilterData> PopularProducts { get; private set; } = [];
    public List<ProductFilterData> NewestProducts { get; private set; } = [];
    public List<BrandDto> Brands { get; private set; } = [];
    public List<BlogPostFilterData> LatestPosts { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Each service uses the same scoped DbContext, so the queries run sequentially.
        var banners = await bannerService.GetActiveBannersAsync();
        HeroBanners = banners.Where(b => b.Position == BannerPosition.HeroSlider).ToList();
        WideBanners = banners.Where(b => b.Position == BannerPosition.WideBanner).ToList();
        PromoBanners = banners.Where(b => b.Position == BannerPosition.PromoCard).ToList();

        Categories = (await categoryService.GetMenuCategoriesAsync()).Take(20).ToList();

        DealProducts = await LoadProductsAsync(ProductSortBy.MostDiscounted, onlyDiscounted: true, onlyAvailable: true);
        PopularProducts = await LoadProductsAsync(ProductSortBy.BestSelling);
        NewestProducts = await LoadProductsAsync(ProductSortBy.Newest);

        Brands = (await brandService.GetActiveBrandsAsync()).Where(b => !string.IsNullOrWhiteSpace(b.LogoName)).ToList();
        LatestPosts = await postService.GetLatestPostsAsync(4);

        await pageMeta.ApplyAsync(ViewData, SitePage.Home, "فروشگاه اینترنتی",
            $"{shopOptions.Value.SiteName}؛ خرید آنلاین کالای دیجیتال با بهترین قیمت، ارسال سریع و ضمانت اصالت کالا.");
    }

    private async Task<List<ProductFilterData>> LoadProductsAsync(ProductSortBy sortBy, bool onlyDiscounted = false, bool onlyAvailable = false)
    {
        var result = await productService.GetProductByFilterAsync(new ProductFilterParams
        {
            OnlyActive = true,
            OnlyDiscounted = onlyDiscounted ? true : null,
            OnlyAvailable = onlyAvailable ? true : null,
            SortBy = sortBy,
            Take = SliderSize,
        });

        return result.Data;
    }
}
