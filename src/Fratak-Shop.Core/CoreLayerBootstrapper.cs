using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Brands;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.SeoRedirects;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Core.Services.Wallets;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Core;

public static class CoreLayerBootstrapper
{
    public static IServiceCollection AddFratakShopCore(this IServiceCollection services)
    {
        // Settings, shipping methods and layout content are read on every request and
        // cached in memory; the services invalidate the cache when the data changes.
        services.AddMemoryCache();

        services.AddScoped<IStoreSettingService, StoreSettingService>();
        services.AddScoped<Services.Seo.ISitemapService, Services.Seo.SitemapService>();
        services.AddScoped<IShippingService, ShippingService>();
        services.AddScoped<IWalletService, WalletService>();
        services.AddScoped<IPaymentTransactionService, PaymentTransactionService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IContentPageService, ContentPageService>();
        services.AddScoped<ISiteContentService, SiteContentService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBrandService, BrandService>();
        services.AddScoped<IBlogCategoryService, BlogCategoryService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IFaqItemService, FaqItemService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IBannerService, BannerService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserAddressService, UserAddressService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderItemService, OrderItemService>();
        services.AddScoped<IPageAlertService, PageAlertService>();
        services.AddScoped<IPageSeoService, PageSeoService>();
        services.AddScoped<ISeoRedirectService, SeoRedirectService>();

        // Engagement: coupons, customer club, notifications, menus, filters, recommendations.
        services.AddScoped<Services.Events.IShopEvents, Services.Events.ShopEvents>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<Services.Loyalty.ILoyaltyService, Services.Loyalty.LoyaltyService>();
        services.AddScoped<Services.Notifications.INotificationService, Services.Notifications.NotificationService>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<Services.Products.IProductFilterService, Services.Products.ProductFilterService>();
        services.AddScoped<Services.Products.IRecommendationService, Services.Products.RecommendationService>();

        return services;
    }
}
