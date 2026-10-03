using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Builds the admin navigation for the current role set. Every item declares which
/// roles may see it, so a SEO specialist only gets the SEO centre (plus read-only
/// products) and a support agent only gets the support screens.
/// </summary>
public static class AdminNavigation
{
    private static readonly UserRole[] FullManagers = [UserRole.SuperAdmin, UserRole.Admin];

    /// <summary>نقش‌هایی که اجازه دیدن محصولات را دارند (کارشناس سئو فقط برای سئوکردن).</summary>
    private static readonly UserRole[] CatalogManagers =
        [UserRole.SuperAdmin, UserRole.Admin, UserRole.ProductManager, UserRole.SeoManager];

    /// <summary>نقش‌هایی که اجازه مدیریت سفارش و پرداخت را دارند.</summary>
    private static readonly UserRole[] SalesManagers =
        [UserRole.SuperAdmin, UserRole.Admin, UserRole.OrderManager, UserRole.SupportAgent];

    public static List<AdminNavItem> Build(List<UserRole> roles, AdminNavCounters counters)
    {
        bool Allowed(UserRole[] allowed) => roles.Any(allowed.Contains);

        var items = new List<AdminNavItem>
        {
            new() { Title = "داشبورد", Path = "/admin", Icon = "home", Exact = true },
        };

        // ------------------------------------------------------------- فروشگاه
        if (Allowed(CatalogManagers))
        {
            items.Add(new AdminNavItem { Title = "محصولات", Path = "/admin/products", Icon = "tag", Group = "فروشگاه" });
            items.Add(new AdminNavItem { Title = "دسته‌بندی‌ها", Path = "/admin/categories", Icon = "bag", Group = "فروشگاه" });
        }

        if (Allowed(SalesManagers))
        {
            items.Add(new AdminNavItem { Title = "سفارش‌ها", Path = "/admin/orders", Icon = "bag", Group = "فروشگاه" });
            items.Add(new AdminNavItem
            {
                Title = "کارت به کارت",
                Path = "/admin/payments/bank-transfers",
                Icon = "tag",
                Badge = counters.PendingBankTransfers,
                Group = "فروشگاه",
            });
            items.Add(new AdminNavItem { Title = "کارت‌های بانکی", Path = "/admin/payments/cards", Icon = "tag", Group = "فروشگاه" });
        }

        if (Allowed(FullManagers) || roles.Contains(UserRole.OrderManager))
        {
            items.Add(new AdminNavItem { Title = "کدهای تخفیف", Path = "/admin/coupons", Icon = "tag", Group = "فروشگاه" });
            items.Add(new AdminNavItem { Title = "روش‌های ارسال", Path = "/admin/shipping", Icon = "bag", Group = "فروشگاه" });
        }

        if (Allowed(FullManagers) || roles.Contains(UserRole.ContentManager) || roles.Contains(UserRole.ProductManager))
            items.Add(new AdminNavItem { Title = "بنرها", Path = "/admin/banners", Icon = "star", Group = "فروشگاه" });

        // -------------------------------------------------------------- محتوا
        if (Allowed(FullManagers) || roles.Contains(UserRole.ContentManager))
        {
            items.Add(new AdminNavItem { Title = "مقالات", Path = "/admin/blog/posts", Icon = "star", Group = "محتوا" });
            items.Add(new AdminNavItem { Title = "دسته‌های وبلاگ", Path = "/admin/blog/categories", Icon = "star", Group = "محتوا" });
            items.Add(new AdminNavItem { Title = "صفحات سایت", Path = "/admin/pages", Icon = "star", Group = "محتوا" });
            items.Add(new AdminNavItem { Title = "پرسش‌های متداول", Path = "/admin/faqs", Icon = "star", Group = "محتوا" });
        }

        if (Allowed(FullManagers) || roles.Contains(UserRole.ContentManager) || roles.Contains(UserRole.SupportAgent))
            items.Add(new AdminNavItem { Title = "دیدگاه‌ها", Path = "/admin/comments", Icon = "star", Group = "محتوا" });

        // ----------------------------------------------------------------- سئو
        if (Allowed(FullManagers) || roles.Contains(UserRole.SeoManager))
        {
            items.Add(new AdminNavItem { Title = "مرکز سئو", Path = "/admin/seo", Icon = "search", Group = "سئو", Exact = true });
            items.Add(new AdminNavItem
            {
                Title = "سئوی هوشمند",
                Path = "/admin/seo/audits",
                Icon = "star",
                Badge = counters.PendingSeoAudits,
                Group = "سئو",
            });
            items.Add(new AdminNavItem { Title = "سئوی صفحات", Path = "/admin/seo/pages", Icon = "star", Group = "سئو" });
            items.Add(new AdminNavItem { Title = "ریدایرکت‌ها", Path = "/admin/seo/redirects", Icon = "search", Group = "سئو" });
            items.Add(new AdminNavItem { Title = "خطاهای ۴۰۴", Path = "/admin/seo/not-found", Icon = "search", Group = "سئو" });
            items.Add(new AdminNavItem { Title = "آمار بازدید", Path = "/admin/seo/statistics", Icon = "star", Group = "سئو" });
        }

        // -------------------------------------------------------------- کاربران
        if (Allowed(FullManagers) || roles.Contains(UserRole.SupportAgent))
        {
            items.Add(new AdminNavItem { Title = "کاربران", Path = "/admin/users", Icon = "user", Group = "کاربران" });
            items.Add(new AdminNavItem { Title = "پیام‌ها", Path = "/admin/messages", Icon = "headphone", Group = "کاربران" });
        }

        if (Allowed(FullManagers))
            items.Add(new AdminNavItem { Title = "اعضای پنل و نقش‌ها", Path = "/admin/users/staff", Icon = "user", Group = "کاربران" });

        items.Add(new AdminNavItem
        {
            Title = "اعلان‌ها",
            Path = "/admin/notifications",
            Icon = "bell",
            Badge = counters.UnreadNotifications,
            Group = "کاربران",
        });

        // ------------------------------------------------------------ تنظیمات
        if (Allowed(FullManagers))
        {
            items.Add(new AdminNavItem { Title = "تنظیمات سایت", Path = "/admin/settings", Icon = "tag", Group = "تنظیمات" });
            items.Add(new AdminNavItem { Title = "منوها و فوتر", Path = "/admin/menus", Icon = "tag", Group = "تنظیمات" });
            items.Add(new AdminNavItem { Title = "شبکه‌های اجتماعی", Path = "/admin/menus/social", Icon = "tag", Group = "تنظیمات" });
            items.Add(new AdminNavItem { Title = "هشدارهای صفحه", Path = "/admin/page-alerts", Icon = "bell", Group = "تنظیمات" });
        }

        return items;
    }
}
