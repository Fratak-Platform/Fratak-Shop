namespace Fratak_Shop.Web.Infrastructure;

/// <summary>کلیدهای سشن و ویو دیتای مشترک لایه وب در یک جا.</summary>
public static class SessionKeys
{
    /// <summary>کلید سبد خرید مهمان؛ در اولین بازدید ساخته می‌شود.</summary>
    public const string Cart = "fratak_cart_key";

    /// <summary>شماره موبایلی که کد تأیید برایش ارسال شده است.</summary>
    public const string PendingPhone = "fratak_pending_phone";

    /// <summary>هدف کد تأیید (ثبت‌نام/بازیابی رمز/ورود بدون رمز).</summary>
    public const string PendingPurpose = "fratak_pending_purpose";

    public const string CheckoutSession = "fratak_checkout";
}

/// <summary>کلیدهای ViewData برای داده‌های مشترک قالب.</summary>
public static class ViewDataKeys
{
    public const string Layout = "Layout";
    public const string Seo = "Seo";
    public const string Title = "Title";
    public const string Message = "Message";
    public const string MessageType = "MessageType";

    /// <summary>فهرست آیتم‌های منوی پنل مدیریت، متناسب با نقش‌های کاربر.</summary>
    public const string AdminNavigation = "AdminNavigation";
}
