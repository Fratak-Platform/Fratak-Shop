using System.Linq.Expressions;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Analytics;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Faqs;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Entities.Shipping;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Contexts;

public partial class FratakShopDbContext(DbContextOptions<FratakShopDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;

    public DbSet<UserAddress> UserAddresses { get; set; } = null!;

    public DbSet<Location> Locations { get; set; } = null!;

    public DbSet<Category> Categories { get; set; } = null!;

    public DbSet<Brand> Brands { get; set; } = null!;

    public DbSet<Product> Products { get; set; } = null!;

    public DbSet<ProductImage> ProductImages { get; set; } = null!;

    public DbSet<ProductFeature> ProductFeatures { get; set; } = null!;

    public DbSet<Banner> Banners { get; set; } = null!;

    public DbSet<Cart> Carts { get; set; } = null!;

    public DbSet<CartItem> CartItems { get; set; } = null!;

    public DbSet<Order> Orders { get; set; } = null!;

    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    public DbSet<Comment> Comments { get; set; } = null!;

    public DbSet<BlogPost> BlogPosts { get; set; } = null!;

    public DbSet<BlogCategory> BlogCategories { get; set; } = null!;

    public DbSet<FaqItem> FaqItems { get; set; } = null!;

    public DbSet<Message> Messages { get; set; } = null!;

    public DbSet<Favorite> Favorites { get; set; } = null!;

    public DbSet<PageSeo> PageSeos { get; set; } = null!;

    public DbSet<PageAlert> PageAlerts { get; set; } = null!;

    public DbSet<SeoRedirect> SeoRedirects { get; set; } = null!;

    public DbSet<StoreSetting> StoreSettings { get; set; } = null!;

    public DbSet<ShippingMethod> ShippingMethods { get; set; } = null!;

    public DbSet<DeliveryTimeSlot> DeliveryTimeSlots { get; set; } = null!;

    public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;

    public DbSet<WithdrawalRequest> WithdrawalRequests { get; set; } = null!;

    public DbSet<PaymentTransaction> PaymentTransactions { get; set; } = null!;

    public DbSet<UserRefreshToken> UserRefreshTokens { get; set; } = null!;

    public DbSet<ContentPage> ContentPages { get; set; } = null!;

    public DbSet<SiteFeature> SiteFeatures { get; set; } = null!;

    public DbSet<HomeSection> HomeSections { get; set; } = null!;

    public DbSet<TrustBadge> TrustBadges { get; set; } = null!;

    public DbSet<FaqCategory> FaqCategories { get; set; } = null!;

    public DbSet<Menu> Menus { get; set; } = null!;

    public DbSet<MenuItem> MenuItems { get; set; } = null!;

    public DbSet<ProductFilterAttribute> ProductFilterAttributes { get; set; } = null!;

    public DbSet<Coupon> Coupons { get; set; } = null!;

    public DbSet<CouponTarget> CouponTargets { get; set; } = null!;

    public DbSet<CouponUsage> CouponUsages { get; set; } = null!;

    public DbSet<LoyaltyRule> LoyaltyRules { get; set; } = null!;

    public DbSet<LoyaltyTier> LoyaltyTiers { get; set; } = null!;

    public DbSet<LoyaltyReward> LoyaltyRewards { get; set; } = null!;

    public DbSet<LoyaltyTransaction> LoyaltyTransactions { get; set; } = null!;

    public DbSet<Notification> Notifications { get; set; } = null!;

    public DbSet<NotificationRead> NotificationReads { get; set; } = null!;

    public DbSet<NotificationTemplate> NotificationTemplates { get; set; } = null!;

    public DbSet<ShippingRate> ShippingRates { get; set; } = null!;

    public DbSet<CommentImage> CommentImages { get; set; } = null!;

    public DbSet<AbandonedCart> AbandonedCarts { get; set; } = null!;

    public DbSet<AbandonedCartReminderStep> AbandonedCartReminderSteps { get; set; } = null!;

    // Round 5
    public DbSet<ProductAlert> ProductAlerts { get; set; } = null!;

    public DbSet<ProductPriceHistory> ProductPriceHistories { get; set; } = null!;

    public DbSet<ProductVideo> ProductVideos { get; set; } = null!;

    public DbSet<GiftCard> GiftCards { get; set; } = null!;

    public DbSet<UserKnownDevice> UserKnownDevices { get; set; } = null!;

    public DbSet<SavedCartItem> SavedCartItems { get; set; } = null!;

    public DbSet<ReturnRequest> ReturnRequests { get; set; } = null!;

    public DbSet<ReturnRequestItem> ReturnRequestItems { get; set; } = null!;

    public DbSet<ReturnRequestImage> ReturnRequestImages { get; set; } = null!;

    public DbSet<ReturnReason> ReturnReasons { get; set; } = null!;

    public DbSet<OrderFeedback> OrderFeedbacks { get; set; } = null!;

    public DbSet<SearchSynonym> SearchSynonyms { get; set; } = null!;

    public DbSet<SearchQueryLog> SearchQueryLogs { get; set; } = null!;

    public DbSet<CommentVote> CommentVotes { get; set; } = null!;

    public DbSet<SupportDepartment> SupportDepartments { get; set; } = null!;

    public DbSet<SupportTicket> SupportTickets { get; set; } = null!;

    public DbSet<SupportTicketMessage> SupportTicketMessages { get; set; } = null!;

    public DbSet<SupportTicketAttachment> SupportTicketAttachments { get; set; } = null!;

    public DbSet<WebPushSubscription> WebPushSubscriptions { get; set; } = null!;

    public DbSet<SeoNotFoundLog> SeoNotFoundLogs { get; set; } = null!;

    // ── Admin panel ──
    public DbSet<AdminRole> AdminRoles { get; set; } = null!;

    public DbSet<AdminRolePermission> AdminRolePermissions { get; set; } = null!;

    public DbSet<UserAdminRole> UserAdminRoles { get; set; } = null!;

    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    public DbSet<ErrorLog> ErrorLogs { get; set; } = null!;

    public DbSet<SecurityLog> SecurityLogs { get; set; } = null!;

    public DbSet<OrderNote> OrderNotes { get; set; } = null!;

    public DbSet<PurchaseInvoice> PurchaseInvoices { get; set; } = null!;

    public DbSet<Supplier> Suppliers { get; set; } = null!;

    /// <summary>مرحلهٔ هفتم: اعلان‌های داخلی پنل برای کارکنان.</summary>
    public DbSet<StaffNotification> StaffNotifications { get; set; } = null!;

    /// <summary>مرحلهٔ هفتم: تنظیمات شخصی هر کارمند در پنل (سنجاق‌ها، فیلترها، ستون‌ها و ...).</summary>
    public DbSet<AdminUserPreference> AdminUserPreferences { get; set; } = null!;

    /// <summary>مرحلهٔ هفتم: پیام‌ها و پاسخ‌های آماده.</summary>
    public DbSet<ReadyMessage> ReadyMessages { get; set; } = null!;

    /// <summary>مرحلهٔ هفتم: متن راهنمای صفحه‌های پنل.</summary>
    public DbSet<AdminHelpArticle> AdminHelpArticles { get; set; } = null!;

    /// <summary>مرحلهٔ هفتم: کتابخانهٔ رسانه (تصویرهای متن محصول، مقاله و صفحات).</summary>
    public DbSet<Fratak_Shop.Data.Entities.Media.MediaFile> MediaFiles { get; set; } = null!;

    /// <summary>پیش‌نویس خودکار فرم‌های ویرایش پنل (مرحلهٔ هفتم – فاز ۳).</summary>
    public DbSet<EditDraft> EditDrafts { get; set; } = null!;

    /// <summary>پیام‌های کارکنان و مشتری دربارهٔ هر سفارش (مرحلهٔ ۷).</summary>
    public DbSet<OrderMessage> OrderMessages { get; set; } = null!;

    /// <summary>برچسب‌های دستی کارکنان روی مشتری‌ها.</summary>
    public DbSet<Fratak_Shop.Data.Entities.Users.CustomerTag> CustomerTags { get; set; } = null!;

    /// <summary>یادداشت‌های داخلی کارکنان دربارهٔ مشتری‌ها.</summary>
    public DbSet<Fratak_Shop.Data.Entities.Users.CustomerNote> CustomerNotes { get; set; } = null!;

    /// <summary>آخرین وضعیت هر محصول در سایت تأمین‌کننده (فاز ۵).</summary>
    public DbSet<SupplierProductState> SupplierProductStates { get; set; } = null!;

    /// <summary>تاریخچهٔ اجراهای بررسی تأمین‌کننده.</summary>
    public DbSet<SupplierSyncRun> SupplierSyncRuns { get; set; } = null!;

    /// <summary>تغییرهای پیدا شده در بررسی تأمین‌کننده (خودکار یا منتظر تأیید).</summary>
    public DbSet<SupplierChange> SupplierChanges { get; set; } = null!;

    /// <summary>دفتر گردش موجودی (مرحلهٔ ۷ – فاز ۵).</summary>
    public DbSet<StockMovement> StockMovements { get; set; } = null!;

    /// <summary>دسته‌های تغییر گروهی قیمت (قابل برگرداندن).</summary>
    public DbSet<PriceChangeBatch> PriceChangeBatches { get; set; } = null!;

    /// <summary>مقدار قبلی/جدید هر محصول در دسته‌های تغییر قیمت.</summary>
    public DbSet<PriceChangeItem> PriceChangeItems { get; set; } = null!;

    /// <summary>رهگیری خودکار مرسوله‌ها (مرحلهٔ ۷ – فاز ۶).</summary>
    public DbSet<ShipmentTracking> ShipmentTrackings { get; set; } = null!;

    /// <summary>رویدادهای خوانده‌شده از سامانهٔ رهگیری پست.</summary>
    public DbSet<ShipmentEvent> ShipmentEvents { get; set; } = null!;

    /// <summary>خلاصهٔ روزانهٔ هر بازدیدکننده: مراحل قیف و منبع ورودی (مرحلهٔ ۷ – فاز ۷).</summary>
    public DbSet<VisitorDay> VisitorDays { get; set; } = null!;

    /// <summary>شمارنده‌های روزانهٔ جمعی (بازدید و افزودن به سبد هر محصول).</summary>
    public DbSet<AnalyticsCounter> AnalyticsCounters { get; set; } = null!;

    // مرحلهٔ هفتم – فاز ۷ب: هزینه‌ها، هدف فروش ماهانه و گزارش‌های زمان‌بندی‌شده.
    public DbSet<Expense> Expenses { get; set; } = null!;

    public DbSet<SalesGoal> SalesGoals { get; set; } = null!;

    public DbSet<ScheduledReport> ScheduledReports { get; set; } = null!;

    public DbSet<ReportDeliveryConfig> ReportDeliveryConfigs { get; set; } = null!;

    // مرحلهٔ هفتم – فاز ۸: موتور سئوی هوشمند داخلی (نمره، صف پیشنهاد، کش تحقیق، تاریخچهٔ اجرا).
    public DbSet<SeoScore> SeoScores { get; set; } = null!;

    public DbSet<SeoSuggestion> SeoSuggestions { get; set; } = null!;

    public DbSet<SeoResearchCache> SeoResearchCaches { get; set; } = null!;

    public DbSet<SeoEngineRun> SeoEngineRuns { get; set; } = null!;

    // مرحلهٔ هفتم – فاز ۸ب: یابندهٔ لینک شکسته، آمار Search Console و تنظیمات ابزارهای سئو.
    public DbSet<SeoLinkIssue> SeoLinkIssues { get; set; } = null!;

    public DbSet<SearchConsoleQueryStat> SearchConsoleQueryStats { get; set; } = null!;

    public DbSet<SearchConsoleDailyStat> SearchConsoleDailyStats { get; set; } = null!;

    public DbSet<SeoToolsConfig> SeoToolsConfigs { get; set; } = null!;

    // مرحلهٔ شانزدهم: جدول دانش سئو/نگارش (یادگیرنده) و بازخورد «پیشنهاد هوشمند».
    public DbSet<SeoKnowledgeEntry> SeoKnowledgeEntries { get; set; } = null!;

    public DbSet<SmartTextFeedback> SmartTextFeedbacks { get; set; } = null!;

    // ── عملیات فنی (مرحلهٔ هفتم – فاز ۹): پشتیبان، دیوار آتش، کنترل کارها، هشدار ──
    public DbSet<Fratak_Shop.Data.Entities.Ops.BackupRecord> BackupRecords { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Ops.IpRule> IpRules { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Ops.BotRule> BotRules { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Ops.BackgroundJobSetting> BackgroundJobSettings { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Ops.AlertRule> AlertRules { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Ops.AlertEvent> AlertEvents { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Team.ApprovalRequest> ApprovalRequests { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Team.StaffTask> StaffTasks { get; set; } = null!;

    // دور هشتم: تنظیمات سرویس پیامک (SMS.ir).
    public DbSet<Fratak_Shop.Data.Entities.Settings.SmsProviderConfig> SmsProviderConfigs { get; set; } = null!;

    // مرحلهٔ دوازدهم: تنظیمات سرویس ایمیل (SMTP).
    public DbSet<Fratak_Shop.Data.Entities.Settings.EmailProviderConfig> EmailProviderConfigs { get; set; } = null!;

    // مرحلهٔ سیزدهم: کش ماندگار قیمت رقبا و صف قیمت‌گذاری بازار محصولات درون‌ریزی‌شده.
    public DbSet<Fratak_Shop.Data.Entities.Products.CompetitorPriceCache> CompetitorPriceCaches { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Products.MarketPriceCheck> MarketPriceChecks { get; set; } = null!;

    // مرحلهٔ پانزدهم: آخرین قیمت رقبای هر محصول (نمایش در فرم محصول و صفحهٔ «رصد قیمت رقبا»).
    public DbSet<Fratak_Shop.Data.Entities.Products.ProductCompetitorSnapshot> ProductCompetitorSnapshots { get; set; } = null!;

    // دور نوزدهم: محتوای هوشمند و فروش هوشمند (Entities/Smart/SmartEntities.cs).
    public DbSet<Fratak_Shop.Data.Entities.Smart.SmartTask> SmartTasks { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.SmartResearchCache> SmartResearchCaches { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.ProductInsight> ProductInsights { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.BlogPlanItem> BlogPlanItems { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.CustomerReminder> CustomerReminders { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.SmartContentConfig> SmartContentConfigs { get; set; } = null!;

    // دور بیستم: بازخورد کارشناس روی عکس‌های پیشنهادی (فهرست سیاه + رتبهٔ منبع).
    public DbSet<Fratak_Shop.Data.Entities.Smart.SmartImageFeedback> SmartImageFeedbacks { get; set; } = null!;

    // دور ۲۱: استودیوی طراحی (بنر/کاور)، صفحه‌ساز کمپین و گزارش رشد خودکار.
    public DbSet<Fratak_Shop.Data.Entities.Design.DesignDoc> DesignDocs { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Design.LandingPage> LandingPages { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Smart.GrowthAction> GrowthActions { get; set; } = null!;

    // ── دور ۲۳: دستیار فروشگاه ──
    public DbSet<Fratak_Shop.Data.Entities.Assistant.AssistantMessage> AssistantMessages { get; set; } = null!;

    // دور ۲۴: سلیقهٔ گفت‌وگو و «گفت‌وگو با کارشناس».
    public DbSet<Fratak_Shop.Data.Entities.Assistant.AssistantProfile> AssistantProfiles { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Assistant.AssistantHandoff> AssistantHandoffs { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Assistant.AssistantHandoffMessage> AssistantHandoffMessages { get; set; } = null!;

    // دور ۲۴ (بستهٔ B): رتبه‌بندی یادگیرنده، جست‌وجوی تصویری و تضمین قیمت.
    public DbSet<Fratak_Shop.Data.Entities.Searches.SearchClickStat> SearchClickStats { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Products.ProductImageSignature> ProductImageSignatures { get; set; } = null!;
    public DbSet<Fratak_Shop.Data.Entities.Marketing.PriceMatchRequest> PriceMatchRequests { get; set; } = null!;

    // دور ۲۴ (بنر هوشمند): قاعدهٔ واقعی تخفیف/امتیاز پشت بنر کمپین.
    public DbSet<Fratak_Shop.Data.Entities.Marketing.CampaignOffer> CampaignOffers { get; set; } = null!;

    /// <summary>Name of the SQL sequence that numbers sales invoices.</summary>
    public const string InvoiceSerialSequence = "InvoiceSerials";

    // دور نوزدهم (A16): رفتار پیش‌فرض «بدون رهگیری تغییرات» (NoTracking) حالا در ثبت DbContext تنظیم می‌شود
    // (FratakShopBootstrapper.AddFratakShopData)، نه در OnConfiguring؛ EF Core با «استخر DbContext» تغییر گزینه‌ها در
    // OnConfiguring را نمی‌پذیرد. هر کد تازه‌ای که DbContext می‌سازد باید UseQueryTrackingBehavior(NoTracking) را خودش بدهد.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity-specific rules (relationships, indexes, check constraints, ...)
        // live in the Configuration classes and stay there.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FratakShopDbContext).Assembly);

        // Gap-free-ish, concurrency safe serial numbers for sales invoices.
        modelBuilder.HasSequence<long>(InvoiceSerialSequence).StartsAt(1).IncrementsBy(1);

        // Cross-cutting soft-delete rules are applied once here so no
        // configuration has to repeat them.
        ApplySoftDeleteRules(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var changed = BeforeSave();
        var added = CollectAdded();
        var seoChanges = CollectSeoChanges();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        DataChangeNotifier.Publish(changed);
        DataChangeNotifier.PublishAdded(added);
        SeoChangeFeed.Publish(seoChanges);
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var changed = BeforeSave();
        var added = CollectAdded();
        var seoChanges = CollectSeoChanges();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        DataChangeNotifier.Publish(changed);
        DataChangeNotifier.PublishAdded(added);
        SeoChangeFeed.Publish(seoChanges);
        return result;
    }

    /// <summary>
    /// موجودیت‌هایی که در این ذخیره «اضافه» می‌شوند (فقط اگر کسی به <see cref="DataChangeNotifier.EntitiesAdded"/> گوش دهد).
    /// باید قبل از ذخیره جمع شوند چون بعد از ذخیره وضعیتشان Unchanged می‌شود.
    /// </summary>
    private IReadOnlyList<object> CollectAdded() =>
        DataChangeNotifier.HasAddedListeners
            ? ChangeTracker.Entries().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList()
            : [];

    /// <summary>Audit dates, price history; returns the changed entity types (for cache invalidation).</summary>
    private HashSet<Type> BeforeSave()
    {
        NormalizePersianLetters();
        TrackPriceChanges();
        TrackStockChanges();
        // دور سئوی خودکار: نامک انگلیسی و ریدایرکت‌های خودکار (پیش از تاریخ‌ها تا ریدایرکت‌های تازه هم شناسه بگیرند).
        ApplySeoRules();
        ApplyAuditDates();

        return ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Metadata.ClrType)
            .ToHashSet();
    }

    /// <summary>
    /// دور ۲۳ (مورد ۳۲): «ي/ى» و «ك» عربی در نام کالا، دسته، برند و عنوان مقاله ← «ی» و «ک» فارسی، هر جا که ذخیره شود (پنل، درون‌ریزی،
    /// تأمین‌کننده). دلیل: جست‌وجوی «علی» نباید «علي» را جا بیندازد و صفحه‌ها یکدست باشند. فقط وقتی مقدار واقعاً عوض شود نوشته می‌شود.
    /// </summary>
    private void NormalizePersianLetters()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            switch (entry.Entity)
            {
                case Product product when PersianLetters(product.Name) is var name && !ReferenceEquals(name, product.Name):
                    product.Name = name;
                    break;
                case Category category when PersianLetters(category.Name) is var name && !ReferenceEquals(name, category.Name):
                    category.Name = name;
                    break;
                case Brand brand when PersianLetters(brand.Name) is var name && !ReferenceEquals(name, brand.Name):
                    brand.Name = name;
                    break;
                case BlogPost post when PersianLetters(post.Title) is var title && !ReferenceEquals(title, post.Title):
                    post.Title = title;
                    break;
            }
        }
    }

    /// <summary>
    /// حروف عربی ← فارسی. اگر چیزی برای تبدیل نبود همان رشته (همان شیء) برمی‌گردد. مثال: «علي» ← «علی»؛ «كيف» ← «کیف».
    /// </summary>
    public static string PersianLetters(string value)
    {
        if (string.IsNullOrEmpty(value) || value.AsSpan().IndexOfAny('\u064A', '\u0649', '\u0643') < 0)
            return value;
        return value.Replace('\u064A', '\u06CC').Replace('\u0649', '\u06CC').Replace('\u0643', '\u06A9');
    }

    /// <summary>
    /// Writes a <see cref="ProductPriceHistory"/> row whenever the price the customer pays (or the
    /// in-stock state) of a product changes – whoever changes it (storefront, admin panel, import).
    /// </summary>
    private void TrackPriceChanges()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Product>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var product = entry.Entity;
            var final = FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
            var inStock = product.StockQuantity > 0;

            if (entry.State == EntityState.Modified)
            {
                var original = entry.OriginalValues;
                var oldFinal = FinalPrice(
                    original.GetValue<decimal>(nameof(Product.Price)),
                    original.GetValue<decimal?>(nameof(Product.DiscountPrice)),
                    original.GetValue<DateTimeOffset?>(nameof(Product.DiscountStartAt)),
                    original.GetValue<DateTimeOffset?>(nameof(Product.DiscountEndAt)),
                    now);
                var oldPrice = original.GetValue<decimal>(nameof(Product.Price));
                var wasInStock = original.GetValue<int>(nameof(Product.StockQuantity)) > 0;

                if (oldFinal == final && oldPrice == product.Price && wasInStock == inStock)
                    continue;
            }

            if (product.Id == Guid.Empty)
                product.Id = Guid.NewGuid();

            ProductPriceHistories.Add(new ProductPriceHistory
            {
                ProductId = product.Id,
                Price = product.Price,
                FinalPrice = final,
                IsInStock = inStock,
            });
        }
    }

    /// <summary>
    /// دفتر گردش موجودی: هر بار که SaveChanges موجودی محصولی را عوض کند یک <see cref="StockMovement"/> می‌سازد.
    /// <para>
    /// دلیل: اول <see cref="StockContext"/> (اگر سرویسی تنظیم کرده)، وگرنه اگر کارمند پنل در کار است «دستی»، وگرنه «سیستم».
    /// تغییرهایی که با ExecuteUpdate انجام می‌شوند از اینجا رد نمی‌شوند و خودشان <see cref="StockLedger"/> را صدا می‌زنند.
    /// </para>
    /// </summary>
    private void TrackStockChanges()
    {
        var info = StockContext.Current;
        var actor = Fratak_Shop.Data.Auditing.AuditContext.Current;

        foreach (var entry in ChangeTracker.Entries<Product>().ToList())
        {
            int delta;
            StockMovementReason reason;
            if (entry.State == EntityState.Added)
            {
                delta = entry.Entity.StockQuantity;
                reason = StockMovementReason.Initial;
            }
            else if (entry.State == EntityState.Modified)
            {
                delta = entry.Entity.StockQuantity - entry.OriginalValues.GetValue<int>(nameof(Product.StockQuantity));
                reason = info?.Reason ?? (actor is not null ? StockMovementReason.Manual : StockMovementReason.System);
            }
            else
            {
                continue;
            }

            if (delta == 0)
                continue;

            if (entry.Entity.Id == Guid.Empty)
                entry.Entity.Id = Guid.NewGuid();

            StockMovements.Add(new StockMovement
            {
                ProductId = entry.Entity.Id,
                Delta = delta,
                BalanceAfter = entry.Entity.StockQuantity,
                Reason = reason,
                OrderId = info?.OrderId,
                Reference = Cut(info?.Reference, 100),
                Note = Cut(info?.Note, 500),
                UserId = actor?.UserId,
                UserName = Cut(actor?.UserName, 200),
            });
        }

        static string? Cut(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
    }

    /// <summary>Same rule as the storefront price calculator (sale price inside its window).</summary>
    public static decimal FinalPrice(decimal price, decimal? discountPrice, DateTimeOffset? start, DateTimeOffset? end, DateTimeOffset now) =>
        discountPrice is { } sale && sale < price && (start is null || start <= now) && (end is null || end >= now)
            ? sale
            : price;

    /// <summary>
    /// Fills CreatedAt/UpdatedAt automatically and turns hard deletes into
    /// soft deletes (IsDeleted/DeletedAt) so nothing is ever physically removed
    /// through SaveChanges.
    /// </summary>
    private void ApplyAuditDates()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();

                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt ??= now;
                    entry.Entity.UpdatedAt = now;
                    entry.State = EntityState.Modified;
                    break;
            }
        }
    }

    /// <summary>
    /// Central soft-delete behavior for every entity inheriting <see cref="BaseEntity"/>:
    /// 1) deleted rows are hidden from queries (global query filter),
    /// 2) unique indexes ignore deleted rows, so a value can be reused after a soft delete.
    /// </summary>
    private static void ApplySoftDeleteRules(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                Expression.Constant(false));
            entityType.SetQueryFilter(Expression.Lambda(body, parameter));

            foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique))
            {
                // Indexes that already declare a soft-delete aware filter keep it.
                if (index.GetFilter()?.Contains("[IsDeleted]", StringComparison.Ordinal) == true)
                    continue;

                index.SetFilter("[IsDeleted] = 0");
            }
        }
    }
}
