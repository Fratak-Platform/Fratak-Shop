using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Checkout;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Content;
using Fratak_Shop.Core.Services.Dashboard;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.SeoRedirects;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Core;

public static class CoreLayerBootstrapper
{
    /// <summary>
    /// Registers every service of the shop. The web layer only calls this method,
    /// so the dependency graph of the application lives in one place.
    /// </summary>
    public static IServiceCollection AddFratakShopCore(this IServiceCollection services)
    {
        // ---------------------------------------------------------------- content
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBlogCategoryService, BlogCategoryService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IFaqItemService, FaqItemService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IFavoriteService, FavoriteService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IBannerService, BannerService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IContentPageService, ContentPageService>();
        services.AddScoped<IMenuService, MenuService>();

        // ------------------------------------------------------------------- shop
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderItemService, OrderItemService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IShippingMethodService, ShippingMethodService>();
        services.AddScoped<ICouponService, CouponService>();

        // --------------------------------------------------------------- payments
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IBankTransferService, BankTransferService>();

        // -------------------------------------------------------------- customers
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserAddressService, UserAddressService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<INotificationService, NotificationService>();

        // -------------------------------------------------------------------- seo
        services.AddScoped<ISeoService, SeoService>();
        services.AddScoped<ISeoStatisticsService, SeoStatisticsService>();
        services.AddScoped<ISeoAuditService, SeoAuditService>();
        services.AddScoped<IPageSeoService, PageSeoService>();
        services.AddScoped<IPageAlertService, PageAlertService>();
        services.AddScoped<ISeoRedirectService, SeoRedirectService>();

        // ------------------------------------------------------- settings & tools
        services.AddScoped<ISiteSettingService, SiteSettingService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISmsSender, LoggingSmsSender>();

        return services;
    }
}
