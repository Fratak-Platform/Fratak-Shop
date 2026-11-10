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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fratak_Shop.Core;

public static class CoreLayerBootstrapper
{
    public static IServiceCollection AddFratakShopCore(this IServiceCollection services)
    {
        // Settings, shipping methods and layout content are read on every request and
        // cached in memory; the services invalidate the cache when the data changes.
        services.AddMemoryCache();
        services.TryAddSingleton<Services.Notifications.IPushSender, Services.Notifications.NullPushSender>();
        // ارسال زنده: پیش‌فرض خالی است؛ لایهٔ Web پیاده‌سازی SignalR را جایگزین می‌کند.
        services.TryAddSingleton<Services.Notifications.IRealtimeNotifier, Services.Notifications.NullRealtimeNotifier>();

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
        // دور ۲۳: دستیار فروشگاه (چت‌بات بدون هوش مصنوعی بیرونی).
        services.AddScoped<Services.Assistant.IAssistantService, Services.Assistant.AssistantService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserAddressService, UserAddressService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IAbandonedCartService, AbandonedCartService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
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
        services.AddScoped<Services.Products.ISearchSuggestService, Services.Products.SearchSuggestService>();
        services.AddScoped<Services.Reports.IReportService, Services.Reports.ReportService>();
        services.AddScoped<Services.Products.IRecommendationService, Services.Products.RecommendationService>();

        // Round 5: alerts, price history, videos, gift cards, devices, «خرید بعدی», returns,
        // shopping-experience rating, smart search, Q&A votes, support tickets, web push, SEO traffic.
        services.AddScoped<IProductAlertService, ProductAlertService>();
        services.AddScoped<IPriceHistoryService, PriceHistoryService>();
        services.AddScoped<IProductVideoService, ProductVideoService>();
        services.AddScoped<IGiftCardService, GiftCardService>();
        services.AddScoped<IKnownDeviceService, KnownDeviceService>();
        services.AddScoped<ISaveForLaterService, SaveForLaterService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IOrderFeedbackService, OrderFeedbackService>();
        services.AddScoped<Services.Searches.ISearchInsightsService, Services.Searches.SearchInsightsService>();
        services.AddScoped<ICommentEngagementService, CommentEngagementService>();
        services.AddScoped<Services.Support.ITicketService, Services.Support.TicketService>();
        services.AddScoped<Services.Notifications.IWebPushService, Services.Notifications.WebPushService>();
        services.AddScoped<Services.Seo.ISeoTrafficService, Services.Seo.SeoTrafficService>();

        // Round 6: admin panel (roles & permissions, monitoring, orders, reports, customers, generic data editor).
        services.AddScoped<Services.Admin.IAdminAccessService, Services.Admin.AdminAccessService>();
        services.AddScoped<Services.Admin.IMonitoringService, Services.Admin.MonitoringService>();
        services.AddScoped<Services.Admin.IAdminDataService, Services.Admin.AdminDataService>();
        services.AddScoped<Services.Admin.IAdminOrderService, Services.Admin.AdminOrderService>();
        services.AddScoped<Services.Admin.IAdminReportService, Services.Admin.AdminReportService>();
        services.AddScoped<Services.Admin.IAdminCustomerService, Services.Admin.AdminCustomerService>();
        services.AddScoped<Services.Admin.ICustomerCrmService, Services.Admin.CustomerCrmService>();
        // بررسی خودکار لینک تأمین‌کننده (مرحلهٔ هفتم – فاز ۵). ISupplierHttp در لایهٔ Web ثبت می‌شود.
        services.AddScoped<Services.Suppliers.ISupplierSyncService, Services.Suppliers.SupplierSyncService>();
        services.AddScoped<Services.Marketing.IOccasionCouponService, Services.Marketing.OccasionCouponService>(); // دور ۲۳: کوپن مناسبتی با تأیید
        services.AddScoped<Services.Pricing.ICompetitorPriceService, Services.Pricing.CompetitorPriceService>();
        // مرحلهٔ پانزدهم: آخرین قیمت رقبای هر محصول (کارت فرم محصول و صفحهٔ «رصد قیمت رقبا»).
        services.AddScoped<Services.Pricing.IProductCompetitorService, Services.Pricing.ProductCompetitorService>();
        // مرحلهٔ سیزدهم: صف/استراحت مشترک منابع بیرونی (ترب، دیجی‌کالا، گوگل…)، کش ماندگار قیمت رقبا و قیمت‌گذاری بازار درون‌ریزی.
        services.AddSingleton<Utilities.IExternalSourceGate>(_ => new Utilities.ExternalSourceGate());
        services.AddSingleton<Services.Pricing.ICompetitorPriceStore, Services.Pricing.CompetitorPriceStore>();
        services.AddScoped<Services.Pricing.IMarketPricingService, Services.Pricing.MarketPricingService>();
        // دور ۱۰: درون‌ریزی گروهی محصولات از سایت دیگر (ووکامرس، شاپیفای، نقشهٔ سایت + JSON-LD).
        services.AddScoped<Services.Importing.ISiteCatalogService, Services.Importing.SiteCatalogService>();
        // رهگیری مرسوله (مرحلهٔ هفتم – فاز ۶). IPostTrackingClient در لایهٔ Web ثبت می‌شود.
        services.AddScoped<Services.Orders.IShipmentTrackingService, Services.Orders.ShipmentTrackingService>();
        // مرحلهٔ هفتم – فاز ۷: آمار بازدید داخلی، قیف فروش، کمپین‌ها، عملکرد محصول و تقاضا.
        services.AddScoped<Services.Reports.IAnalyticsService, Services.Reports.AnalyticsService>();
        services.AddScoped<Services.Reports.IAnalyticsReportService, Services.Reports.AnalyticsReportService>();

        // مرحلهٔ هفتم – فاز ۷ب: هزینه‌ها، هدف فروش، عملکرد کارکنان و گزارش‌های زمان‌بندی‌شده.
        // IReportDelivery (ارسال ایمیل/ربات) در لایهٔ Web ثبت می‌شود.
        services.AddScoped<Services.Insights.IExpenseService, Services.Insights.ExpenseService>();
        services.AddScoped<Services.Insights.ISalesGoalService, Services.Insights.SalesGoalService>();
        services.AddScoped<Services.Insights.IStaffPerformanceService, Services.Insights.StaffPerformanceService>();
        services.AddScoped<Services.Insights.IScheduledReportService, Services.Insights.ScheduledReportService>();

        // مرحلهٔ هفتم – فاز ۸: موتور هوشمند سئو (تحلیل، نوشتن، تحقیق رقبا، پیشنهاد و تأیید).
        // منبع وب پیش‌فرض «آفلاین» است؛ لایهٔ Web پیاده‌سازی HTTP واقعی را جایگزین می‌کند.
        services.TryAddSingleton<Services.Seo.Engine.ISeoWebSource, Services.Seo.Engine.OfflineSeoWebSource>();
        services.AddScoped<Services.Seo.Engine.ISeoEngineService, Services.Seo.Engine.SeoEngineService>();
        // مرحلهٔ شانزدهم: جدول دانش سئو (کش حافظه + یادگیری با scope جداگانه) و «پیشنهاد هوشمند» متن.
        services.AddSingleton<Services.Seo.Knowledge.SeoKnowledgeCache>();
        services.AddSingleton<Services.Seo.Knowledge.ISeoKnowledgeService, Services.Seo.Knowledge.SeoKnowledgeService>();
        services.AddScoped<Services.Seo.Smart.ISmartTextService, Services.Seo.Smart.SmartTextService>();
        // دور سئوی خودکار (۲۱۰ ایده): خلبان خودکار، خزندهٔ داخلی، لینک داخلی، نگهداری (نامک انگلیسی/WebP)، گزارش، فرودها و API ترب.
        // پیاده‌سازی‌های شبکه‌ای (خزش، IndexNow) و دامنهٔ عمومی را لایهٔ Web جایگزین می‌کند.
        services.TryAddSingleton<Services.Seo.Autopilot.ISeoHost, Services.Seo.Autopilot.UnknownSeoHost>();
        services.TryAddSingleton<Services.Seo.Autopilot.ISeoPinger, Services.Seo.Autopilot.NullSeoPinger>();
        services.TryAddSingleton<Services.Seo.Autopilot.ISeoPageFetcher, Services.Seo.Autopilot.OfflineSeoPageFetcher>();
        services.AddScoped<Services.Seo.Autopilot.ISeoAutopilotService, Services.Seo.Autopilot.SeoAutopilotService>();
        services.AddScoped<Services.Seo.Autopilot.ISeoCrawlService, Services.Seo.Autopilot.SeoCrawlService>();
        services.AddScoped<Services.Seo.Autopilot.ISeoLinkTargetProvider, Services.Seo.Autopilot.SeoLinkTargetProvider>();
        services.AddScoped<Services.Seo.Autopilot.ISeoMaintenanceService, Services.Seo.Autopilot.SeoMaintenanceService>();
        services.AddScoped<Services.Seo.Autopilot.ISeoReportService, Services.Seo.Autopilot.SeoReportService>();
        services.AddScoped<Services.Seo.Autopilot.ISeoLandingService, Services.Seo.Autopilot.SeoLandingService>();
        services.AddScoped<Services.Seo.Feeds.ITorobFeedService, Services.Seo.Feeds.TorobFeedService>();

        // فاز ۸ب: یابندهٔ لینک شکسته، ویرایش گروهی سئو و Search Console. پیش‌فرض‌های «آفلاین» (بدون شبکه) را
        // لایهٔ Web با SeoLinkProbe و SearchConsoleHttp جایگزین می‌کند.
        services.TryAddSingleton<Services.Seo.Tools.ISeoLinkProbe, Services.Seo.Tools.OfflineSeoLinkProbe>();
        services.TryAddSingleton<Services.Seo.Tools.ISearchConsoleHttp, Services.Seo.Tools.OfflineSearchConsoleHttp>();
        services.AddScoped<Services.Seo.Tools.ISeoLinkAuditService, Services.Seo.Tools.SeoLinkAuditService>();
        services.AddScoped<Services.Seo.Tools.ISeoBulkEditService, Services.Seo.Tools.SeoBulkEditService>();
        services.AddScoped<Services.Seo.Tools.ISearchConsoleService, Services.Seo.Tools.SearchConsoleService>();

        // فاز ۹ (فنی): فایروال IP/ربات، پشتیبان‌گیری، کنترل کارهای پس‌زمینه و هشدارها.
        // بایگانی آپلودها پیش‌فرض «غیرفعال» است؛ لایهٔ Web پیاده‌سازی zip واقعی (UploadsArchiver) را جایگزین می‌کند.
        services.TryAddSingleton<Services.Ops.IUploadsArchiver, Services.Ops.NullUploadsArchiver>();
        services.AddScoped<Services.Ops.IFirewallService, Services.Ops.FirewallService>();
        services.AddScoped<Services.Ops.IBackupService, Services.Ops.BackupService>();
        services.AddScoped<Services.Ops.IBackgroundJobControl, Services.Ops.BackgroundJobControl>();
        services.AddScoped<Services.Ops.IAlertService, Services.Ops.AlertService>();
        // دور هشتم: تنظیمات سرویس پیامک (SMS.ir) – ارسال واقعی در لایهٔ وب (ConfigurableSmsSender).
        services.AddScoped<Services.Notifications.ISmsProviderService, Services.Notifications.SmsProviderService>();
        // مرحلهٔ دوازدهم: تنظیمات سرویس ایمیل (SMTP) – صف و ارسال واقعی در لایهٔ وب (EmailQueue)؛ اینجا نسخه‌های بی‌اثر برای آزمون/ابزار.
        services.AddScoped<Services.Notifications.IEmailProviderService, Services.Notifications.EmailProviderService>();
        services.TryAddSingleton<Services.Notifications.IEmailSender, Services.Notifications.NullEmailSender>();
        services.TryAddSingleton<Services.Notifications.IEmailServerSecret, Services.Notifications.NoEmailServerSecret>();

        // مرحلهٔ هفتم – فاز ۱۰: امنیت و کار تیمی کارکنان (TOTP، تأیید دونفره، منشن، کارهای شخصی).
        services.AddScoped<Services.Team.IStaffTotpService, Services.Team.StaffTotpService>();
        services.AddScoped<Services.Team.IApprovalService, Services.Team.ApprovalService>();
        services.AddScoped<Services.Team.IMentionService, Services.Team.MentionService>();
        services.AddScoped<Services.Team.IStaffTaskService, Services.Team.StaffTaskService>();

        // ابزارهای کاتالوگ (فاز ۵): تغییر گروهی قیمت، انبار و پیش‌بینی، ورود از اکسل، کپی محصول.
        services.AddScoped<Services.Products.IBulkPriceService, Services.Products.BulkPriceService>();
        services.AddScoped<Services.Products.IInventoryService, Services.Products.InventoryService>();
        services.AddScoped<Services.Products.IProductImportService, Services.Products.ProductImportService>();
        services.AddScoped<Services.Products.IProductCloneService, Services.Products.ProductCloneService>();

        // مرحلهٔ هفتم: اعلان کارکنان، پیام‌های آماده و تنظیمات شخصی پنل.
        services.AddScoped<Services.Admin.IStaffNotificationService, Services.Admin.StaffNotificationService>();
        services.AddScoped<Services.Admin.IReadyMessageService, Services.Admin.ReadyMessageService>();
        services.AddScoped<Services.Admin.IAdminPreferenceService, Services.Admin.AdminPreferenceService>();
        // مرحلهٔ هفتم (P2): میانبرها، راهنمای صفحه‌ها، کتابخانهٔ رسانه و جست‌وجوی سریع پنل.
        services.AddScoped<Services.Admin.IAdminShortcutService, Services.Admin.AdminShortcutService>();
        services.AddScoped<Services.Admin.IAdminHelpService, Services.Admin.AdminHelpService>();
        services.AddScoped<Services.Admin.IMediaLibraryService, Services.Admin.MediaLibraryService>();
        // مرحلهٔ هفتم – فاز ۳: پیش‌نویس خودکار فرم‌ها و تاریخچه/بازگردانی تغییرات.
        services.AddScoped<Services.Admin.IEditDraftService, Services.Admin.EditDraftService>();
        services.AddScoped<Services.Admin.IAuditHistoryService, Services.Admin.AuditHistoryService>();
        services.AddScoped<Services.Admin.IAdminSearchService, Services.Admin.AdminSearchService>();
        // مرحلهٔ هفتم – فاز ۴: زمان‌بندی سفارش، قوانین پرداخت در محل، پیام سفارش، لینک پرداخت و مشتریان.
        services.AddScoped<Services.Orders.IOrderSlaService, Services.Orders.OrderSlaService>();
        services.AddScoped<Services.Orders.ICashOnDeliveryPolicy, Services.Orders.CashOnDeliveryPolicy>();
        services.AddScoped<Services.Orders.IOrderMessageService, Services.Orders.OrderMessageService>();
        services.AddScoped<Services.Orders.IStaffOrderService, Services.Orders.StaffOrderService>();

        // دور نوزدهم (B/C): محتوای هوشمند، صف کارها، خلاصهٔ خرید، برنامهٔ بلاگ، یادآوری مشتری، نگاه خریدار و خلاصهٔ روزانه.
        services.AddScoped<Services.Smart.ISmartConfigService, Services.Smart.SmartConfigService>();
        services.AddScoped<Services.Smart.ISmartTaskQueue, Services.Smart.SmartTaskQueue>();
        services.AddScoped<Services.Smart.ISmartResearchStore, Services.Smart.SmartResearchStore>();
        services.AddScoped<Services.Smart.IProductInsightService, Services.Smart.ProductInsightService>();
        services.AddScoped<Services.Smart.ISmartCatalogService, Services.Smart.SmartCatalogService>();
        // دور ۲۱: دسته‌بندی هوشمند کالاهای درون‌ریزی‌شده (بدون مدل زبانی).
        services.AddScoped<Services.Smart.ISmartCategoryService, Services.Smart.SmartCategoryService>();
        // دور ۲۲: ورود هوشمند کالا (دسته‌بندی چند دقیقه بعد از ساخت + صف «تکمیل هوشمند» + سئوی بعد از انتشار).
        services.AddScoped<Services.Smart.IProductIntakeService, Services.Smart.ProductIntakeService>();
        services.AddScoped<Services.Smart.ICategoryTreeService, Services.Smart.CategoryTreeService>(); // دور ۲۳: ممیز ساعتی درخت دسته‌ها
        services.AddScoped<Services.Products.ISpecFacetService, Services.Products.SpecFacetService>(); // دور ۲۳: فیلتر مشخصات خودکار
        services.AddScoped<Services.Smart.IBlogPlanService, Services.Smart.BlogPlanService>();
        services.AddScoped<Services.Smart.ICustomerReminderService, Services.Smart.CustomerReminderService>();
        services.AddScoped<Services.Smart.IBuyerGuideService, Services.Smart.BuyerGuideService>();
        services.AddScoped<Services.Smart.ISalesBriefService, Services.Smart.SalesBriefService>();
        // دور ۲۱: رشد خودکار روزانه، استودیوی طراحی (بنر/کاور بدون هوش مصنوعی بیرونی) و صفحه‌ساز کمپین.
        services.AddScoped<Services.Smart.IGrowthAutopilot, Services.Smart.GrowthAutopilot>();
        services.AddScoped<Services.Design.IDesignService, Services.Design.DesignService>();
        services.AddScoped<Services.Design.ILandingPageService, Services.Design.LandingPageService>();

        return services;
    }
}
