using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Fratak_Shop.Core.Services.Shipping;

namespace Fratak_Shop.Web.Pages;

// Review photos: up to ReviewMaxImages × ReviewMaxImageSizeKb (both capped) plus the form fields.
[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
[RequestFormLimits(MultipartBodyLengthLimit = 32 * 1024 * 1024)]
[RequestSizeLimit(32 * 1024 * 1024)]
public class ProductModel(
    IProductService productService,
    ICommentService commentService,
    IFavoriteService favoriteService,
    ICompareListService compareList,
    IPageMetaService pageMeta,
    IShippingService shippingService,
    IRecommendationService recommendations,
    IReviewImageStore imageStore,
    IProductVideoService videoService,
    IProductAlertService alertService,
    ICommentEngagementService engagement,
    ISiteContext site) : ShopPageModel
{
    public List<ProductVideoDto> Videos { get; private set; } = [];

    /// <summary>«خبرم کن» subscriptions of the signed-in customer (guests: none).</summary>
    public Core.DTOs.Engagement.ProductAlertState AlertState { get; private set; } = new(false, false, null);

    /// <summary>Helpful votes of the signed-in customer by comment id (true = helpful).</summary>
    public Dictionary<Guid, bool> MyVotes { get; private set; } = [];

    public ProductDto Product { get; private set; } = null!;
    public List<CommentDto> Reviews { get; private set; } = [];
    public List<CommentDto> Questions { get; private set; } = [];
    public List<ProductFilterData> RelatedProducts { get; private set; } = [];
    public List<ProductFilterData> BoughtTogether { get; private set; } = [];
    public List<ProductFilterData> ForYou { get; private set; } = [];
    public bool IsFavorite { get; private set; }
    public bool IsInCompare { get; private set; }

    /// <summary>Share of reviewers who recommend the product (null = no data).</summary>
    public int? RecommendPercent { get; private set; }

    /// <summary>Fastest delivery of the active shipping methods (days, 0 = same day).</summary>
    public int? MinDeliveryDays { get; private set; }

    /// <summary>Lowest free-shipping threshold of the active methods (null = no free shipping).</summary>
    public decimal? FreeShippingFrom { get; private set; }

    [BindProperty]
    public ReviewInput Review { get; set; } = new();

    /// <summary>The signed-in customer already reviewed this product (form replaced by a note).</summary>
    public bool HasReviewed { get; private set; }

    /// <summary>The signed-in customer received this product (shown next to the form).</summary>
    public bool HasPurchased { get; private set; }

    [BindProperty]
    public QuestionInput Question { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        if (!await LoadAsync(slug))
            return NotFound();

        if (site.Settings.IsRecommendationEnabled)
        {
            var viewed = RecentlyViewed.Read(Request);
            BoughtTogether = await recommendations.GetBoughtTogetherAsync(Product.Id, 6);
            var exclude = BoughtTogether.Select(p => p.Id).Concat(RelatedProducts.Select(p => p.Id)).Append(Product.Id).ToList();
            ForYou = await recommendations.GetPersonalizedAsync(CurrentUserId, viewed, exclude, 10);
        }

        // Visit count and "recently viewed" come from the view beacon of shop.js (/api/products/{id}/view),
        // so they also work when this page is served from the page cache.
        return Page();
    }

    public async Task<IActionResult> OnPostReviewAsync(string slug, List<IFormFile>? reviewImages)
    {
        if (!IsSignedIn)
            return RedirectToLogin($"/product/{slug}#comments");

        var product = await productService.GetProductBySlugAsync(slug);
        if (product is null || !product.IsActive)
            return NotFound();

        ModelState.Clear();
        if (!TryValidateModel(Review, nameof(Review)))
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
                           ?? "اطلاعات دیدگاه معتبر نیست.";
            return Redirect($"/product/{slug}#comments");
        }

        var userId = CurrentUserId!.Value;
        if (await commentService.HasReviewedAsync(userId, product.Id))
        {
            ErrorMessage = "شما قبلاً برای این محصول دیدگاه ثبت کرده‌اید.";
            return Redirect($"/product/{slug}#comments");
        }

        List<CommentImageInput> images = [];
        var files = reviewImages ?? [];
        if (files.Any(f => f.Length > 0))
        {
            var settings = site.Settings;
            if (!settings.IsReviewImageEnabled)
            {
                ErrorMessage = "ارسال تصویر برای دیدگاه‌ها فعال نیست.";
                return Redirect($"/product/{slug}#comments");
            }

            var (saved, error) = await imageStore.SaveAsync(files, Math.Clamp(settings.ReviewMaxImages, 0, 10),
                settings.ReviewMaxImageSizeKb, HttpContext.RequestAborted);
            if (error is not null)
            {
                ErrorMessage = error;
                return Redirect($"/product/{slug}#comments");
            }

            images = saved;
        }

        var result = await commentService.CreateCommentAsync(new CreateCommentCommand
        {
            UserId = userId,
            ProductId = product.Id,
            Type = CommentType.Review,
            AuthorName = User.GetDisplayName(),
            AuthorPhone = User.GetPhoneNumber() ?? "-",
            Title = Review.Title?.Trim(),
            Content = Review.Content.Trim(),
            Rating = Review.Rating,
            IsRecommended = Review.IsRecommended,
            Images = images,
        });

        if (!Succeeded(result))
            imageStore.Delete(images);

        ShowResult(result);
        return Redirect($"/product/{slug}#comments");
    }

    public async Task<IActionResult> OnPostQuestionAsync(string slug)
    {
        if (!IsSignedIn)
            return RedirectToLogin($"/product/{slug}#quest");

        var product = await productService.GetProductBySlugAsync(slug);
        if (product is null || !product.IsActive)
            return NotFound();

        ModelState.Clear();
        if (!TryValidateModel(Question, nameof(Question)))
        {
            ErrorMessage = "متن پرسش باید بین ۵ تا ۱۰۰۰ کاراکتر باشد.";
            return Redirect($"/product/{slug}#quest");
        }

        var result = await commentService.CreateCommentAsync(new CreateCommentCommand
        {
            UserId = CurrentUserId!.Value,
            ProductId = product.Id,
            Type = CommentType.Question,
            AuthorName = User.GetDisplayName(),
            AuthorPhone = User.GetPhoneNumber() ?? "-",
            Content = Question.Content.Trim(),
        });

        if (Succeeded(result))
            result.Message = "پرسش شما ثبت شد و پس از بررسی همراه پاسخ نمایش داده می‌شود.";

        ShowResult(result);
        return Redirect($"/product/{slug}#quest");
    }

    private async Task<bool> LoadAsync(string slug)
    {
        var product = await productService.GetProductBySlugAsync(slug);
        if (product is null || !product.IsActive)
            return false;

        Product = product;

        var methods = await shippingService.GetActiveMethodsAsync(HttpContext.RequestAborted);
        if (methods.Count > 0)
        {
            MinDeliveryDays = methods.Min(m => m.MinDeliveryDays);
            var thresholds = methods
                .Select(m => m.FreeShippingThreshold ?? site.Settings.FreeShippingThreshold)
                .Where(t => t > 0)
                .ToList();
            FreeShippingFrom = thresholds.Count > 0 ? thresholds.Min() : null;
        }

        if (CurrentUserId is { } userId)
        {
            HasReviewed = await commentService.HasReviewedAsync(userId, product.Id);
            HasPurchased = await commentService.HasPurchasedAsync(userId, product.Id);
        }

        var comments = await commentService.GetCommentsByProductAsync(product.Id, onlyApproved: true);
        Reviews = comments.Where(c => c.Type != CommentType.Question).ToList();
        Questions = comments.Where(c => c.Type == CommentType.Question).ToList();

        var withOpinion = Reviews.Where(r => r.IsRecommended.HasValue).ToList();
        if (withOpinion.Count > 0)
            RecommendPercent = (int)Math.Round(withOpinion.Count(r => r.IsRecommended == true) * 100d / withOpinion.Count);

        RelatedProducts = await productService.GetRelatedProductsAsync(product.Id, 10);

        if (CurrentUserId.HasValue)
            IsFavorite = await favoriteService.IsFavoriteAsync(CurrentUserId.Value, product.Id);

        IsInCompare = compareList.GetProductIds().Contains(product.Id);
        Videos = await videoService.GetForProductAsync(product.Id);

        if (CurrentUserId is { } signedIn)
        {
            if (site.Settings.IsProductAlertEnabled)
                AlertState = await alertService.GetStateAsync(signedIn, product.Id);

            if (site.Settings.IsCommentVotingEnabled)
            {
                var ids = comments.Select(c => c.Id).Concat(comments.SelectMany(c => c.Replies).Select(r => r.Id)).ToList();
                MyVotes = await engagement.GetUserVotesAsync(signedIn, ids);
            }
        }

        var image = product.Images.FirstOrDefault()?.ImageName;
        pageMeta.Apply(ViewData, product.Seo, product.Name,
            product.ShortDescription ?? Format.Excerpt(product.Description, 160),
            ImageHelper.Product(image), "product");

        ViewData[PageMetaKeys.JsonLd] ??= BuildJsonLd(product);
        var crumbs = new List<(string Name, string? Path)>();
        if (!string.IsNullOrWhiteSpace(product.CategoryName) && !string.IsNullOrWhiteSpace(product.CategorySlug))
            crumbs.Add((product.CategoryName, "/category/" + product.CategorySlug));
        crumbs.Add((product.Name, null));
        pageMeta.AddBreadcrumbs(ViewData, crumbs.ToArray());
        return true;
    }

    private string BuildJsonLd(ProductDto product)
    {
        var request = HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";
        var url = $"{baseUrl}/product/{product.Slug}";
        var settings = site.Settings;

        string Absolute(string path) => path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : baseUrl + path;

        var images = product.Images.Count > 0
            ? product.Images.Take(6).Select(i => Absolute(ImageHelper.Product(i.ImageName))).ToList()
            : new List<string> { Absolute(ImageHelper.ProductPlaceholder) };

        var offer = new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
            ["url"] = url,
            ["priceCurrency"] = "IRR",
            ["price"] = product.FinalPrice * 10,
            ["priceValidUntil"] = product.HasActiveDiscount && product.DiscountEndAt is { } end ? end.ToString("yyyy-MM-dd") : null,
            ["itemCondition"] = "https://schema.org/NewCondition",
            ["availability"] = product.IsInStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
            ["seller"] = new Dictionary<string, object> { ["@type"] = "Organization", ["name"] = settings.SiteName },
        };

        if (settings.IsReturnEnabled && settings.ReturnWindowDays > 0)
        {
            offer["hasMerchantReturnPolicy"] = new Dictionary<string, object>
            {
                ["@type"] = "MerchantReturnPolicy",
                ["applicableCountry"] = "IR",
                ["returnPolicyCategory"] = "https://schema.org/MerchantReturnFiniteReturnWindow",
                ["merchantReturnDays"] = settings.ReturnWindowDays,
                ["returnMethod"] = "https://schema.org/ReturnByMail",
            };
        }

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["@id"] = url + "#product",
            ["name"] = product.Name,
            ["alternateName"] = product.EnglishName,
            ["url"] = url,
            ["sku"] = product.SKU,
            ["gtin"] = string.IsNullOrWhiteSpace(product.Gtin) ? null : product.Gtin,
            ["mpn"] = string.IsNullOrWhiteSpace(product.Mpn) ? null : product.Mpn,
            ["image"] = images,
            ["description"] = product.ShortDescription ?? Format.Excerpt(product.Description, 300),
            ["category"] = product.CategoryName,
            ["brand"] = product.BrandName is null ? null : new Dictionary<string, object> { ["@type"] = "Brand", ["name"] = product.BrandName },
            ["offers"] = offer.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value),
        };

        // A few recent reviews (Google shows them as review snippets).
        var reviews = Reviews.Where(r => r.Rating.HasValue).Take(5).Select(r => new Dictionary<string, object?>
        {
            ["@type"] = "Review",
            ["author"] = new Dictionary<string, object> { ["@type"] = "Person", ["name"] = r.AuthorName },
            ["datePublished"] = r.CreatedAt.ToString("yyyy-MM-dd"),
            ["reviewBody"] = Format.Excerpt(r.Content, 500),
            ["reviewRating"] = new Dictionary<string, object> { ["@type"] = "Rating", ["ratingValue"] = r.Rating!.Value, ["bestRating"] = 5, ["worstRating"] = 1 },
        }).ToList();
        if (reviews.Count > 0)
            data["review"] = reviews;

        if (Videos.Count > 0)
        {
            data["subjectOf"] = Videos.Take(3).Select(v => new Dictionary<string, object?>
            {
                ["@type"] = "VideoObject",
                ["name"] = v.Title ?? product.Name,
                ["description"] = v.Title ?? product.ShortDescription ?? product.Name,
                ["thumbnailUrl"] = v.PosterImage is { } poster ? Absolute(ImageHelper.Product(poster)) : images[0],
                ["uploadDate"] = v.CreatedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
                ["duration"] = v.DurationSeconds is { } seconds ? System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(seconds)) : null,
                ["contentUrl"] = v.FileUrl is { } file ? Absolute(file) : null,
                ["embedUrl"] = v.EmbedUrl,
            }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value)).ToList();
        }

        if (product.ReviewCount > 0 && product.AverageRating > 0)
        {
            data["aggregateRating"] = new Dictionary<string, object>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = product.AverageRating,
                ["reviewCount"] = product.ReviewCount,
            };
        }

        var clean = data.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value);
        return JsonSerializer.Serialize(clean, PageMetaService.JsonLdOptions);
    }

    public class ReviewInput
    {
        [MaxLength(250, ErrorMessage = "عنوان دیدگاه حداکثر ۲۵۰ کاراکتر است.")]
        public string? Title { get; set; }

        [Required(ErrorMessage = "متن دیدگاه را وارد کنید.")]
        [StringLength(2000, MinimumLength = 5, ErrorMessage = "متن دیدگاه باید بین ۵ تا ۲۰۰۰ کاراکتر باشد.")]
        public string Content { get; set; } = string.Empty;

        [Range(1, 5, ErrorMessage = "امتیاز باید بین ۱ تا ۵ باشد.")]
        public byte? Rating { get; set; }

        public bool? IsRecommended { get; set; }
    }

    public class QuestionInput
    {
        [Required]
        [StringLength(1000, MinimumLength = 5)]
        public string Content { get; set; } = string.Empty;
    }
}
