using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Payments;
using Fratak_Shop.Data.Entities.Seo;

namespace Fratak_Shop.Web.Utilities;

/// <summary>
/// Presentation helpers shared by every view of the storefront and the admin panel:
/// image addresses, Persian titles and the colour of status badges. Keeping them here
/// means a status colour never has to be repeated (or invented) inside a Razor file.
/// </summary>
public static class Ui
{
    private const string ProductFallback = "/assets/image/products/1.webp";
    private const string CategoryFallback = "/assets/image/category/1.webp";
    private const string BlogFallback = "/assets/image/blog/1.jpg";

    // ------------------------------------------------------------------ images
    public static string ProductImage(string? imageName) =>
        string.IsNullOrWhiteSpace(imageName) ? ProductFallback : $"/uploads/products/{imageName}";

    public static string CategoryImage(string? imageName) =>
        string.IsNullOrWhiteSpace(imageName) ? CategoryFallback : $"/uploads/categories/{imageName}";

    public static string BlogImage(string? imageName) =>
        string.IsNullOrWhiteSpace(imageName) ? BlogFallback : $"/uploads/blog/{imageName}";

    public static string BannerImage(string? imageName) =>
        string.IsNullOrWhiteSpace(imageName) ? "/assets/image/banner/1.webp" : $"/uploads/banners/{imageName}";

    public static string AvatarImage(string? imageName) =>
        string.IsNullOrWhiteSpace(imageName) ? "/assets/image/man.webp" : $"/uploads/avatars/{imageName}";

    public static string Logo(StorefrontSettings site) =>
        string.IsNullOrWhiteSpace(site.LogoImageName) ? "/assets/image/logo.png" : $"/uploads/header/{site.LogoImageName}";

    public static string FooterLogo(StorefrontSettings site) =>
        string.IsNullOrWhiteSpace(site.FooterLogoImageName)
            ? Logo(site)
            : $"/uploads/footer/{site.FooterLogoImageName}";

    public static string Favicon(StorefrontSettings site) =>
        string.IsNullOrWhiteSpace(site.FaviconImageName) ? "/assets/image/logo.png" : $"/uploads/header/{site.FaviconImageName}";

    /// <summary>آدرس فایل‌های آپلودشده با پوشه دلخواه (برای فیش‌ها و تصاویر آگهی).</summary>
    public static string Upload(string folder, string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"/uploads/{folder}/{fileName}";

    // ------------------------------------------------------------------- money
    public static string Money(decimal value) => value.ToToman();

    public static string Money(decimal? value) => value.ToToman();

    public static string Number(int value) => PriceHelper.ToPersianDigits(value.ToString("#,0"));

    public static string Number(decimal value) => PriceHelper.ToPersianDigits(value.ToString("#,0"));

    // ---------------------------------------------------------------- statuses
    public static string OrderStatusClass(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "badge-amber",
        OrderStatus.Confirmed => "badge-sky",
        OrderStatus.Processing => "badge-violet",
        OrderStatus.Shipped => "badge-sky",
        OrderStatus.Delivered => "badge-green",
        OrderStatus.Cancelled => "badge-red",
        OrderStatus.Returned => "badge-gray",
        _ => "badge-gray",
    };

    public static string OrderStatusTitle(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "در انتظار پرداخت",
        OrderStatus.Confirmed => "تأییدشده",
        OrderStatus.Processing => "در حال آماده‌سازی",
        OrderStatus.Shipped => "ارسال‌شده",
        OrderStatus.Delivered => "تحویل‌شده",
        OrderStatus.Cancelled => "لغوشده",
        OrderStatus.Returned => "مرجوع‌شده",
        _ => "نامشخص",
    };

    public static string PaymentStatusClass(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "badge-green",
        PaymentStatus.Pending => "badge-amber",
        PaymentStatus.Failed => "badge-red",
        PaymentStatus.Refunded => "badge-violet",
        _ => "badge-gray",
    };

    public static string PaymentStatusTitle(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "پرداخت‌شده",
        PaymentStatus.Pending => "در انتظار پرداخت",
        PaymentStatus.Failed => "ناموفق",
        PaymentStatus.Refunded => "بازگشت وجه",
        _ => "پرداخت نشده",
    };

    public static string PaymentMethodTitle(PaymentMethod method) => method switch
    {
        PaymentMethod.OnlineGateway => "پرداخت آنلاین",
        PaymentMethod.CashOnDelivery => "پرداخت در محل",
        PaymentMethod.BankTransfer => "کارت به کارت",
        PaymentMethod.Wallet => "کیف پول",
        _ => "نامشخص",
    };

    public static string BankTransferStatusClass(BankTransferStatus status) => status switch
    {
        BankTransferStatus.AwaitingCustomerInfo => "badge-gray",
        BankTransferStatus.AwaitingReview => "badge-amber",
        BankTransferStatus.Approved => "badge-green",
        BankTransferStatus.Rejected => "badge-red",
        BankTransferStatus.Expired => "badge-gray",
        _ => "badge-gray",
    };

    public static string BankTransferStatusTitle(BankTransferStatus status) => status switch
    {
        BankTransferStatus.AwaitingCustomerInfo => "در انتظار ثبت اطلاعات",
        BankTransferStatus.AwaitingReview => "در انتظار تأیید",
        BankTransferStatus.Approved => "تأییدشده",
        BankTransferStatus.Rejected => "ردشده",
        BankTransferStatus.Expired => "منقضی",
        _ => "نامشخص",
    };

    public static string CommentStatusClass(CommentStatus status) => status switch
    {
        CommentStatus.Approved => "badge-green",
        CommentStatus.Pending => "badge-amber",
        CommentStatus.Rejected => "badge-red",
        _ => "badge-gray",
    };

    public static string CommentStatusTitle(CommentStatus status) => status switch
    {
        CommentStatus.Approved => "تأییدشده",
        CommentStatus.Pending => "در انتظار تأیید",
        CommentStatus.Rejected => "ردشده",
        _ => "نامشخص",
    };

    public static string MessageStatusClass(MessageStatus status) => status switch
    {
        MessageStatus.New => "badge-sky",
        MessageStatus.InProgress => "badge-amber",
        MessageStatus.Answered => "badge-green",
        MessageStatus.Closed => "badge-gray",
        _ => "badge-gray",
    };

    public static string MessageStatusTitle(MessageStatus status) => status switch
    {
        MessageStatus.New => "جدید",
        MessageStatus.InProgress => "در حال بررسی",
        MessageStatus.Answered => "پاسخ داده‌شده",
        MessageStatus.Closed => "بسته‌شده",
        _ => "نامشخص",
    };

    public static string MessageTypeTitle(MessageType type) => type switch
    {
        MessageType.Contact => "تماس با ما",
        MessageType.Support => "پشتیبانی",
        MessageType.Suggestion => "پیشنهاد",
        MessageType.Complaint => "شکایت",
        _ => "سایر",
    };

    public static string CouponStatusClass(bool isActive, bool isExpired, bool isStarted, bool isExhausted) => !isActive
        ? "badge-gray"
        : isExpired
            ? "badge-red"
            : !isStarted
                ? "badge-sky"
                : isExhausted
                    ? "badge-amber"
                    : "badge-green";

    public static string CouponStatusTitle(bool isActive, bool isExpired, bool isStarted, bool isExhausted) => !isActive
        ? "غیرفعال"
        : isExpired
            ? "منقضی"
            : !isStarted
                ? "زمان‌بندی‌شده"
                : isExhausted
                    ? "تمام‌شده"
                    : "فعال";

    public static string AlertClass(AlertType type) => type switch
    {
        AlertType.Success => "alert-success",
        AlertType.Warning => "alert-warning",
        AlertType.Error => "alert-danger",
        _ => "alert-info",
    };

    public static string NotificationClass(NotificationType type) => type switch
    {
        NotificationType.Success => "alert-success",
        NotificationType.Warning => "alert-warning",
        NotificationType.Error => "alert-danger",
        _ => "alert-info",
    };

    public static string SeoScoreClass(int score) => score switch
    {
        >= 80 => "text-emerald-500",
        >= 60 => "text-amber-500",
        _ => "text-rose-500",
    };

    public static string SeoScoreBadgeClass(int score) => score switch
    {
        >= 80 => "badge-green",
        >= 60 => "badge-amber",
        _ => "badge-red",
    };

    public static string SeoGradeTitle(int score) => score switch
    {
        >= 90 => "عالی",
        >= 80 => "خوب",
        >= 60 => "قابل قبول",
        >= 40 => "ضعیف",
        _ => "بحرانی",
    };

    public static string SeverityClass(SeoFindingSeverity severity) => severity switch
    {
        SeoFindingSeverity.Critical => "badge-red",
        SeoFindingSeverity.Warning => "badge-amber",
        SeoFindingSeverity.Success => "badge-green",
        _ => "badge-gray",
    };

    /// <summary>عنوان فارسی نوع هشدار صفحه (اطلاع‌رسانی، موفقیت، هشدار، خطا).</summary>
    public static string AlertTitle(AlertType type) => type switch
    {
        AlertType.Success => "موفقیت",
        AlertType.Warning => "هشدار",
        AlertType.Error => "خطا",
        AlertType.Info => "اطلاع‌رسانی",
        _ => "نامشخص",
    };

    /// <summary>رنگ متناسب با نوع اعلان برای نوار کناری پنل.</summary>
    public static string StatTile(string color) => color switch
    {
        "sky" => "bg-sky-100 text-sky-600",
        "green" => "bg-emerald-100 text-emerald-600",
        "amber" => "bg-amber-100 text-amber-600",
        "red" => "bg-rose-100 text-rose-600",
        "violet" => "bg-violet-100 text-violet-600",
        "blue" => "bg-blue-100 text-blue-600",
        "orange" => "bg-orange-100 text-orange-600",
        _ => "bg-zinc-100 text-zinc-600",
    };

    // ------------------------------------------------------------ site pages
    /// <summary>آدرس عمومی هر صفحه سایت؛ منبع یکتای لینک‌دهی به صفحه‌ها.</summary>
    public static string SitePageUrl(SitePage page) => page switch
    {
        SitePage.Home => "/",
        SitePage.Products => "/products",
        SitePage.ProductDetails => "/product",
        SitePage.Categories => "/categories",
        SitePage.CategoryDetails => "/category",
        SitePage.Blog => "/blog",
        SitePage.BlogPostDetails => "/blog",
        SitePage.Cart => "/cart",
        SitePage.Checkout => "/checkout",
        SitePage.OrderTracking => "/profile/orders",
        SitePage.Search => "/search",
        SitePage.ContactUs => "/contact",
        SitePage.AboutUs => "/about",
        SitePage.Faq => "/faq",
        SitePage.Warranty => "/warranty",
        SitePage.Terms => "/rules",
        SitePage.Privacy => "/privacy",
        SitePage.Login => "/auth/login",
        SitePage.Register => "/auth/register",
        SitePage.UserProfile => "/profile",
        SitePage.UserAddresses => "/profile/addresses",
        SitePage.UserOrders => "/profile/orders",
        SitePage.UserFavorites => "/profile/favorites",
        SitePage.NotFound => "/status/404",
        _ => "/",
    };
}
