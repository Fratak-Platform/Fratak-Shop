using Fratak_Shop.Core.Services.Notifications;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services.AddScoped<ISiteContext, SiteContext>();
        services.AddScoped<SiteContextPageFilter>();
        services.AddScoped<ICartSessionService, CartSessionService>();
        services.AddScoped<IShopAuthService, ShopAuthService>();
        services.AddScoped<ICompareListService, CompareListService>();
        services.AddScoped<IPageMetaService, PageMetaService>();
        services.AddScoped<IPaymentFlowService, PaymentFlowService>();
        services.AddScoped<DemoDataSeeder>();

        // ZarinPal API client (typed HttpClient: pooled handlers, short timeout).
        services.AddHttpClient<IPaymentGateway, ZarinPalGateway>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Fratak-Shop/1.0");
        });

        services.AddSingleton<IOtpService, OtpService>();
        services.AddSingleton<IReviewImageStore, ReviewImageStore>();
        services.AddSingleton<ISmsSender, LoggingSmsSender>();
        services.AddSingleton<Infrastructure.Auth.ITwoFactorTicket, Infrastructure.Auth.TwoFactorTicket>();

        services.AddHostedService<OrderMaintenanceService>();

        // Round 5: captcha, secrets, uploads, web push, background jobs.
        services.AddSingleton<Infrastructure.Security.ICaptchaService, Infrastructure.Security.CaptchaService>();
        services.AddSingleton<Core.Services.Security.ISecretProtector, Infrastructure.Engagement.DataProtectionSecretProtector>();
        services.AddSingleton<IUploadStore, UploadStore>();
        services.AddSingleton<Infrastructure.Engagement.ChannelPushSender>();
        services.Replace(ServiceDescriptor.Singleton<IPushSender>(sp => sp.GetRequiredService<Infrastructure.Engagement.ChannelPushSender>()));
        services.AddHttpClient(Infrastructure.Engagement.WebPushDispatcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Fratak-Shop/1.0");
        });
        services.AddHostedService<Infrastructure.Engagement.WebPushDispatcher>();
        services.AddHostedService<Infrastructure.Engagement.EngagementMaintenanceService>();

        // Round 6: admin panel – permission filter, live activity, error logging to the database,
        // health checks, background-job monitor and log retention.
        services.AddScoped<Infrastructure.Admin.AdminPageFilter>();
        services.AddScoped<Infrastructure.Admin.AdminBadgeService>();
        services.AddScoped<Infrastructure.Admin.Manage.ManageEngine>();
        services.AddSingleton<Infrastructure.Admin.ActivityTracker>();
        services.AddSingleton<Infrastructure.Admin.ErrorLogQueue>();
        services.AddSingleton<ILoggerProvider, Infrastructure.Admin.DbErrorLoggerProvider>();
        services.AddSingleton<Infrastructure.Admin.HealthService>();
        services.AddHostedService<Infrastructure.Admin.ErrorLogWriter>();
        services.AddHostedService<Infrastructure.Admin.MonitoringMaintenanceService>();

        return services;
    }
}
