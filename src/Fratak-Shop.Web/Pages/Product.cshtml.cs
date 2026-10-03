using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.DTOs.Pages.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class ProductModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IProductService products,
    ICommentService comments,
    IFavoriteService favorites,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public ProductDetailDto Product { get; private set; } = null!;

    public List<CommentDto> Reviews { get; private set; } = [];

    public bool IsFavorite { get; private set; }

    public List<PageAlertDto> Alerts { get; private set; } = [];

    [BindProperty]
    public ReviewInput Review { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var product = await products.GetDetailBySlugAsync(slug);
        if (product is null)
            return NotFound();

        Product = product;
        Reviews = await comments.GetCommentsByProductAsync(product.Id);

        if (CurrentUserId is { } userId)
            IsFavorite = (await favorites.GetFavoritesByUserIdAsync(userId)).Any(item => item.ProductId == product.Id);

        Alerts = await pageAlerts.GetVisibleAsync(SitePage.ProductDetails);

        await products.RegisterVisitAsync(product.Id);

        var imageUrl = $"{Site.SiteUrl}{Ui.ProductImage(product.ImageName)}";
        ApplySeo(product.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            DefaultShareImageName = Site.DefaultShareImageName,
            Path = $"/product/{product.Slug}",
            ContentTitle = product.Name,
            ShortDescription = product.ShortDescription,
            ImageName = product.ImageName,
            ImageFolder = "products",
            ImageAlt = product.ImageAlt,
            EntityType = SeoEntityType.Product,
            AutoSchemaJsonLd = SeoJsonLd.Product(product, Site.SiteUrl, Site.SiteName, imageUrl,
                product.RatingValue, product.RatingCount),
        });

        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(string slug)
    {
        var product = await products.GetDetailBySlugAsync(slug);
        if (product is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            Notify("اطلاعات دیدگاه کامل نیست.", "error");
            return Redirect($"/product/{slug}#reviews");
        }

        var command = new CreateCommentCommand
        {
            UserId = CurrentUserId ?? Guid.Empty,
            ProductId = product.Id,
            Type = CommentType.Review,
            AuthorName = Review.AuthorName,
            AuthorPhone = Review.AuthorPhone,
            Title = Review.Title,
            Content = Review.Content,
            Rating = (byte?)Review.Rating,
            IsRecommended = Review.Rating >= 4,
        };

        var result = await comments.CreateCommentAsync(command);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");

        return Redirect($"/product/{slug}#reviews");
    }

    public class ReviewInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام خود را وارد کنید.")]
        public string AuthorName { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string AuthorPhone { get; set; } = null!;

        public string? Title { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن دیدگاه را وارد کنید.")]
        public string Content { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Range(1, 5, ErrorMessage = "امتیاز را انتخاب کنید.")]
        public int Rating { get; set; } = 5;
    }
}
