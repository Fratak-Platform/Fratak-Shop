using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Services.Favorites;
using Fratak_Shop.Core.Services.Locations;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.SeoRedirects;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Core;

public static class CoreLayerBootstrapper
{
    public static IServiceCollection AddFratakShopCore(this IServiceCollection services)
    {
        services.AddScoped<ICategoryService, CategoryService>();
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

        return services;
    }
}
