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

        return services;
    }
}
