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

        // مرحلهٔ هفتم – فاز ۵: بررسی خودکار لینک تأمین‌کننده (HttpClient با محافظ SSRF + صف اجرای فوری + زمان‌بند).
        services.AddHttpClient(Infrastructure.Suppliers.SupplierHttp.ClientName, Infrastructure.Suppliers.SupplierHttp.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.Suppliers.SupplierHttp.CreateHandler);
        services.AddSingleton<Core.Services.Suppliers.ISupplierHttp, Infrastructure.Suppliers.SupplierHttp>();
        services.AddSingleton<Infrastructure.Suppliers.SupplierSyncQueue>();
        services.AddHostedService<Infrastructure.Suppliers.SupplierSyncWorker>();

        // مرحلهٔ هفتم – فاز ۶: رهگیری خودکار مرسوله‌های پستی (سامانهٔ عمومی پست + صف اجرای فوری + زمان‌بند).
        services.AddHttpClient(Infrastructure.Shipping.PostTrackingClient.ClientName, Infrastructure.Shipping.PostTrackingClient.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.Shipping.PostTrackingClient.CreateHandler);
        services.AddSingleton<Core.Services.Orders.IPostTrackingClient, Infrastructure.Shipping.PostTrackingClient>();
        services.AddSingleton<Infrastructure.Shipping.PostTrackingQueue>();
        services.AddHostedService<Infrastructure.Shipping.PostTrackingWorker>();
        // مرحلهٔ هفتم – فاز ۷: آمار بازدید داخلی (بافر حافظه + ذخیرهٔ دقیقه‌ای + ابزار صفحه‌ها).
        services.AddSingleton<Infrastructure.Analytics.AnalyticsBuffer>();
        services.AddScoped<Infrastructure.Analytics.AnalyticsTracker>();
        services.AddHostedService<Infrastructure.Analytics.AnalyticsFlushWorker>();

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
        // مرحلهٔ هفتم: پاک کردن کش صفحه‌ها در لحظهٔ شروع/پایان حراج زمان‌دار.
        services.AddHostedService<Infrastructure.PageCache.DiscountBoundaryWatcher>();

        // مرحلهٔ هفتم: اتصال زنده (SignalR) برای اعلان لحظه‌ای سایت و پنل + هشدار ویرایش هم‌زمان.
        services.AddSignalR(options =>
        {
            // پیام‌های مرورگر → سرور فقط کلید ویرایش است؛ سقف کوچک جلوی سوءاستفاده را می‌گیرد.
            options.MaximumReceiveMessageSize = 4 * 1024;
            options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        });
        services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, Infrastructure.Realtime.ShopUserIdProvider>();
        services.Replace(ServiceDescriptor.Singleton<Core.Services.Notifications.IRealtimeNotifier, Infrastructure.Realtime.SignalRRealtimeNotifier>());
        services.AddHostedService<Infrastructure.Realtime.StaffAlertDispatcher>();

        // Round 6: admin panel – permission filter, live activity, error logging to the database,
        // health checks, background-job monitor and log retention.
        services.AddScoped<Infrastructure.Admin.AdminPageFilter>();
        services.AddScoped<Infrastructure.Admin.AdminBadgeService>();
        services.AddScoped<Infrastructure.Admin.Manage.ManageEngine>();
        services.AddSingleton<Infrastructure.Admin.EditPresenceNotifier>();
        services.AddSingleton<Infrastructure.Admin.ActivityTracker>();
        services.AddSingleton<Infrastructure.Admin.SecurityEventRecorder>();
        services.AddSingleton<Infrastructure.Admin.ErrorLogQueue>();
        services.AddSingleton<ILoggerProvider, Infrastructure.Admin.DbErrorLoggerProvider>();
        services.AddSingleton<Infrastructure.Admin.HealthService>();
        services.AddHostedService<Infrastructure.Admin.ErrorLogWriter>();
        services.AddHostedService<Infrastructure.Admin.MonitoringMaintenanceService>();

        return services;
    }
}
