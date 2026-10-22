using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Extensions;

public static class WebServiceCollectionExtensions
{
    public static IServiceCollection AddFratakShopWeb(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICartSessionService, CartSessionService>();
        services.AddScoped<ISeoService, SeoService>();

        return services;
    }
}
