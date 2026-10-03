using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Users;

/// <summary>
/// Single source of truth for role metadata: Persian titles shown in the admin
/// panel, whether the role belongs to the staff area and which area of the panel
/// it may open. Permission policies themselves are declared in the web layer;
/// this class only carries presentation/classification data so no UI ever has to
/// hard-code a role name again.
/// </summary>
public static class UserRoleExtensions
{
    /// <summary>
    /// Roles a super admin can hand out (everything except the storefront-only roles
    /// and the super admin role itself, which is protected).
    /// </summary>
    public static readonly UserRole[] AssignableRoles =
    [
        UserRole.Admin,
        UserRole.ProductManager,
        UserRole.OrderManager,
        UserRole.ContentManager,
        UserRole.SeoManager,
        UserRole.SupportAgent,
        UserRole.Customer,
    ];

    /// <summary>
    /// Every role of the system together with its Persian title.
    /// </summary>
    public static readonly (UserRole Role, string Title)[] AllRolesWithTitles =
    [
        (UserRole.SuperAdmin, "مدیر ارشد"),
        (UserRole.Admin, "مدیر"),
        (UserRole.ProductManager, "مدیر محصولات"),
        (UserRole.OrderManager, "مدیر سفارش‌ها"),
        (UserRole.ContentManager, "مدیر محتوا و وبلاگ"),
        (UserRole.SeoManager, "کارشناس سئو"),
        (UserRole.SupportAgent, "پشتیبان"),
        (UserRole.Customer, "مشتری"),
    ];

    /// <summary>
    /// Persian title of the role.
    /// </summary>
    public static string GetTitle(this UserRole role) => role switch
    {
        UserRole.SuperAdmin => "مدیر ارشد",
        UserRole.Admin => "مدیر",
        UserRole.ProductManager => "مدیر محصولات",
        UserRole.OrderManager => "مدیر سفارش‌ها",
        UserRole.ContentManager => "مدیر محتوا و وبلاگ",
        UserRole.SeoManager => "کارشناس سئو",
        UserRole.SupportAgent => "پشتیبان",
        UserRole.Customer => "مشتری",
        _ => "کاربر مهمان",
    };

    /// <summary>
    /// Short description of the role shown next to its title in the admin panel.
    /// </summary>
    public static string GetDescription(this UserRole role) => role switch
    {
        UserRole.SuperAdmin => "دسترسی کامل به تمام بخش‌های پنل، تنظیمات و مدیریت نقش‌ها",
        UserRole.Admin => "مدیریت محصولات، سفارش‌ها، محتوا و کاربران بدون دسترسی به تنظیمات حساس",
        UserRole.ProductManager => "افزودن و ویرایش محصولات، دسته‌بندی‌ها و موجودی انبار",
        UserRole.OrderManager => "بررسی سفارش‌ها، تغییر وضعیت ارسال و مدیریت پرداخت‌ها",
        UserRole.ContentManager => "مدیریت مقالات، دسته‌بندی وبلاگ، دیدگاه‌ها و محتوای صفحات",
        UserRole.SeoManager => "مدیریت سئوی محصولات، مقالات و صفحات، ریدایرکت‌ها و نقشه سایت",
        UserRole.SupportAgent => "پاسخ به پیام‌ها، دیدگاه‌ها و پیگیری سفارش‌های مشتریان",
        UserRole.Customer => "خرید، ثبت سفارش و مدیریت پروفایل شخصی",
        _ => "بدون دسترسی",
    };

    /// <summary>
    /// True for every role that may enter the admin area.
    /// </summary>
    public static bool IsStaff(this UserRole role) => role is not (UserRole.None or UserRole.Customer);

    /// <summary>
    /// Landing page of the admin panel for the given role
    /// (the SEO specialist lands on the SEO centre, the order manager on orders, ...).
    /// </summary>
    public static string GetAdminLandingPath(this UserRole role) => role switch
    {
        UserRole.SeoManager => "/admin/seo",
        UserRole.OrderManager => "/admin/orders",
        UserRole.ProductManager => "/admin/products",
        UserRole.ContentManager => "/admin/blog",
        UserRole.SupportAgent => "/admin/messages",
        _ => "/admin",
    };
}
