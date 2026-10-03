namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Authorization policies of the shop. Every area of the admin panel is protected by
/// the policy that matches its role, and the super admin passes all of them.
/// </summary>
public static class PolicyNames
{
    /// <summary>هر عضو پنل (مدیر، مدیر محصولات، سئو، پشتیبان و ...).</summary>
    public const string StaffOnly = "StaffOnly";

    /// <summary>مدیر ارشد و مدیر: دسترسی کامل به پنل.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>حذف و عملیات حساس، مخصوص مدیر ارشد.</summary>
    public const string SuperAdminOnly = "SuperAdminOnly";

    /// <summary>مدیریت محصولات و دسته‌بندی‌ها.</summary>
    public const string Products = "ProductsAccess";

    /// <summary>مدیریت سفارش‌ها، پرداخت‌ها و کارت به کارت.</summary>
    public const string Orders = "OrdersAccess";

    /// <summary>مدیریت محتوا، وبلاگ و صفحات.</summary>
    public const string Content = "ContentAccess";

    /// <summary>مرکز سئو، ریدایرکت‌ها و نقشه سایت.</summary>
    public const string Seo = "SeoAccess";

    /// <summary>پیام‌ها، دیدگاه‌ها و پشتیبانی.</summary>
    public const string Support = "SupportAccess";

    /// <summary>تنظیمات سایت، منوها و کاربران پنل.</summary>
    public const string Settings = "SettingsAccess";

    /// <summary>مشتری وارد‌شده به حساب.</summary>
    public const string CustomerOnly = "CustomerOnly";
}

/// <summary>نام کلیم‌های هویت کاربر در کوکی احراز هویت.</summary>
public static class FratakClaims
{
    public const string UserId = "fratak:user_id";
    public const string FullName = "fratak:full_name";
    public const string Avatar = "fratak:avatar";
}
