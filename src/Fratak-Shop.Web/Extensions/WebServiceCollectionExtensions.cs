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
        // دور هشتم: پیامک واقعی با SMS.ir بر اساس تنظیمات پنل («فنی ← سرویس پیامک»)؛ بدون کلید فقط لاگ می‌شود.
        services.AddHttpClient(Infrastructure.Sms.SmsIrClient.ClientName, Infrastructure.Sms.SmsIrClient.Configure);
        services.AddSingleton<Infrastructure.Sms.SmsDiagnostics>();
        services.AddSingleton<Infrastructure.Sms.ConfigurableSmsSender>();
        services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<Infrastructure.Sms.ConfigurableSmsSender>());
        services.AddSingleton<Infrastructure.Auth.ITwoFactorTicket, Infrastructure.Auth.TwoFactorTicket>();
        // مرحلهٔ دوازدهم: سرویس ایمیل (SMTP با MailKit) بر اساس تنظیمات پنل («فنی ← سرویس ایمیل») – صف حافظه + ارسال پس‌زمینه.
        services.AddSingleton<Core.Services.Notifications.IEmailServerSecret, Infrastructure.Email.ConfigurationEmailServerSecret>();
        services.AddSingleton<Infrastructure.Email.EmailDiagnostics>();
        services.AddSingleton<Infrastructure.Email.EmailQueue>();
        services.AddSingleton<Core.Services.Notifications.IEmailSender>(sp => sp.GetRequiredService<Infrastructure.Email.EmailQueue>());
        services.AddSingleton<Infrastructure.Email.EmailDelivery>();
        services.AddHostedService<Infrastructure.Email.EmailDispatcher>();
        services.AddScoped<Infrastructure.Email.EmailVerification>();

        services.AddHostedService<OrderMaintenanceService>();

        // مرحلهٔ هفتم – فاز ۵: بررسی خودکار لینک تأمین‌کننده (HttpClient با محافظ SSRF + صف اجرای فوری + زمان‌بند).
        services.AddHttpClient(Infrastructure.Suppliers.SupplierHttp.ClientName, Infrastructure.Suppliers.SupplierHttp.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.Suppliers.SupplierHttp.CreateHandler);
        services.AddSingleton<Core.Services.Suppliers.ISupplierHttp, Infrastructure.Suppliers.SupplierHttp>();
        services.AddSingleton<Infrastructure.Suppliers.SupplierSyncQueue>();
        services.AddHostedService<Infrastructure.Suppliers.SupplierSyncWorker>();
        // مرحلهٔ سیزدهم: قیمت‌گذاری خودکار محصولات درون‌ریزی‌شده بر اساس قیمت بازار.
        services.AddHostedService<Infrastructure.Suppliers.MarketPricingWorker>();

        // مرحلهٔ هفتم – فاز ۶: رهگیری خودکار مرسوله‌های پستی (سامانهٔ عمومی پست + صف اجرای فوری + زمان‌بند).
        services.AddHttpClient(Infrastructure.Shipping.PostTrackingClient.ClientName, Infrastructure.Shipping.PostTrackingClient.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.Shipping.PostTrackingClient.CreateHandler);
        // مرحلهٔ هفتم – اصلاحیه: سایت پست کد امنیتی (کپچا) می‌خواهد؛ خواندن آن با مدل آفلاین ONNX (رایگان، بدون سرویس بیرونی).
        services.AddSingleton<Core.Services.Orders.IPostCaptchaSolver, Infrastructure.Shipping.OnnxCaptchaSolver>();
        services.AddSingleton<Core.Services.Orders.IPostTrackingClient, Infrastructure.Shipping.PostTrackingClient>();
        services.AddSingleton<Infrastructure.Shipping.PostTrackingQueue>();
        services.AddHostedService<Infrastructure.Shipping.PostTrackingWorker>();

        // مرحلهٔ هفتم – فاز ۸: موتور هوشمند سئو (منابع رایگان اینترنتی با هندلر امن + صف اجرای فوری + زمان‌بند).
        // ثبت AddSingleton بعد از TryAdd لایهٔ Core، منبع «آفلاین» پیش‌فرض را جایگزین می‌کند.
        services.AddHttpClient(Infrastructure.SeoEngine.SeoWebSource.ClientName, Infrastructure.SeoEngine.SeoWebSource.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.SeoEngine.SeoWebSource.CreateHandler)
            .RemoveAllLoggers();
        services.AddSingleton<Core.Services.Seo.Engine.ISeoWebSource, Infrastructure.SeoEngine.SeoWebSource>();
        services.AddSingleton<Infrastructure.SeoEngine.SeoEngineQueue>();
        services.AddHostedService<Infrastructure.SeoEngine.SeoEngineWorker>();
        // مرحلهٔ شانزدهم: ریختن دادهٔ اولیهٔ جدول دانش سئو در پس‌زمینه (یک بار برای هر نسخهٔ داده).
        services.AddHostedService<Infrastructure.SeoEngine.SeoKnowledgeSeedWorker>();

        // فاز ۸ب: ابزارهای سئو — بررسی لینک (هندلر امن بدون ریدایرکت خودکار)، اتصال Search Console و سرویس پس‌زمینه.
        services.AddHttpClient(Infrastructure.SeoEngine.SeoLinkProbe.ClientName, Infrastructure.SeoEngine.SeoLinkProbe.Configure)
            .ConfigurePrimaryHttpMessageHandler(Infrastructure.SeoEngine.SeoLinkProbe.CreateHandler)
            .RemoveAllLoggers();
        services.AddSingleton<Core.Services.Seo.Tools.ISeoLinkProbe, Infrastructure.SeoEngine.SeoLinkProbe>();
        services.AddSingleton<Core.Services.Seo.Tools.ISearchConsoleHttp, Infrastructure.SeoEngine.SearchConsoleHttp>();
        services.AddSingleton<Infrastructure.SeoEngine.SeoToolsQueue>();
        services.AddHostedService<Infrastructure.SeoEngine.SeoToolsWorker>();

        // گزارش‌های خودکار (مرحلهٔ ۷ – فاز ۷ب): ارسال ایمیل/بله/تلگرام و سرویس پس‌زمینه.
        // RemoveAllLoggers: آدرس API ربات توکن را در خود دارد؛ لاگ پیش‌فرض HttpClient نباید آن را ثبت کند.
        services.AddHttpClient(Infrastructure.Insights.ReportDelivery.BotClientName, Infrastructure.Insights.ReportDelivery.Configure)
            .RemoveAllLoggers();
        services.AddScoped<Core.Services.Insights.IReportDelivery, Infrastructure.Insights.ReportDelivery>();
        services.AddHostedService<Infrastructure.Insights.InsightsWorker>();
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
        }).ConfigurePrimaryHttpMessageHandler(() =>
        {
            // مرحلهٔ هفتم – اصلاحیه: سرورهای داخل ایران معمولاً به fcm.googleapis.com (پوش کروم/اندروید) دسترسی ندارند.
            // با «WebPush:Proxy» در appsettings یا متغیر محیطی WebPush__Proxy (مثلاً http://127.0.0.1:8118) فقط ترافیک پوش
            // از پراکسی خروجی می‌رود؛ خالی = اتصال مستقیم (پیش‌فرض).
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10), ConnectTimeout = TimeSpan.FromSeconds(8) };
            if (Uri.TryCreate(configuration["WebPush:Proxy"], UriKind.Absolute, out var proxy) && proxy.Scheme is "http" or "https" or "socks5")
            {
                handler.Proxy = new System.Net.WebProxy(proxy);
                handler.UseProxy = true;
            }
            return handler;
        });
        // Singleton + HostedService از همان نمونه: صفحهٔ /admin/inbox برای «اعلان آزمایشی» SendNowAsync را مستقیم صدا می‌زند.
        services.AddSingleton<Infrastructure.Engagement.WebPushDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<Infrastructure.Engagement.WebPushDispatcher>());
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
        services.AddSingleton<Infrastructure.Auth.DefaultAdminPasswords>(); // مرحلهٔ ۱۴: هشدار رمز اولیهٔ مدیران پیش‌فرض
        services.AddHostedService<Infrastructure.Admin.ErrorLogWriter>();
        services.AddHostedService<Infrastructure.Admin.MonitoringMaintenanceService>();

        // مرحلهٔ هفتم – فاز ۹ (فنی): نمایشگر گزارش‌ها، پشتیبان آپلودها، شاخص‌های هشدار و سرویس‌های پس‌زمینه.
        services.AddSingleton<Infrastructure.Ops.LogStore>();
        services.AddSingleton<ILoggerProvider, Infrastructure.Ops.MemoryLogProvider>();
        services.AddSingleton<Infrastructure.Ops.UploadsArchiver>();
        services.Replace(ServiceDescriptor.Singleton<Core.Services.Ops.IUploadsArchiver>(sp => sp.GetRequiredService<Infrastructure.Ops.UploadsArchiver>()));
        services.AddScoped<Core.Services.Ops.IOpsMetricsSource, Infrastructure.Ops.OpsMetricsSource>();
        services.AddHostedService<Infrastructure.Ops.OpsWorker>();
        services.AddHostedService<Infrastructure.Ops.BackupWorker>();
        // آمار کش حافظه (تعداد، برخورد/عدم برخورد) برای کارت «کش» در /admin/system.
        services.Configure<Microsoft.Extensions.Caching.Memory.MemoryCacheOptions>(options => options.TrackStatistics = true);

        // مرحلهٔ هفتم – فاز ۱۰ (امنیت و کار تیمی کارکنان): تأیید دوباره، پوشاندن اطلاعات و یادآوری کارها.
        services.AddSingleton<Infrastructure.Team.IReauthTicket, Infrastructure.Team.ReauthTicket>();
        services.AddScoped<Infrastructure.Team.SensitiveData>();
        services.AddHostedService<Infrastructure.Team.TeamWorker>();

        return services;
    }
}
