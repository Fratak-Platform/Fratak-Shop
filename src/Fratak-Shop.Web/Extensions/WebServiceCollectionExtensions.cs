using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Seeders;
using Fratak_Shop.Web.Services;
using Fratak_Shop.Web.Services.Payments;

namespace Fratak_Shop.Web.Extensions;

public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registers the presentation-layer services of the storefront.
    /// </summary>
    public static IServiceCollection AddFratakShopWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShopOptions>(configuration.GetSection(ShopOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddMemoryCache();

        services.AddScoped<ICartSessionService, CartSessionService>();
        services.AddScoped<IShopAuthService, ShopAuthService>();
        services.AddScoped<ICompareListService, CompareListService>();
        services.AddScoped<IPageMetaService, PageMetaService>();
        services.AddScoped<IPaymentGateway, SandboxPaymentGateway>();
        services.AddScoped<DemoDataSeeder>();

        services.AddSingleton<IOtpService, OtpService>();
        services.AddSingleton<ISmsSender, LoggingSmsSender>();

        return services;
    }
}
