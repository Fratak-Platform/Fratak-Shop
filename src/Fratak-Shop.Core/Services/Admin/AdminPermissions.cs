namespace Fratak_Shop.Core.Services.Admin;

/// <summary>One permission of the admin panel, shown as a checkbox in the role editor.</summary>
public sealed record PermissionInfo(string Key, string Group, string Title, string? Description = null);

/// <summary>
/// The permission catalog of the admin panel. Roles store these keys; pages and actions check them.
/// Keys are stable strings (stored in the database) – never rename an existing key.
/// </summary>
public static class AdminPermissions
{
    public const string Dashboard = "dashboard.view";

    public const string OrdersView = "orders.view";
    public const string OrdersManage = "orders.manage";
    public const string OrdersCancel = "orders.cancel";
    public const string PurchaseInvoices = "orders.purchase-invoices";

    /// <summary>ثبت سفارش از پنل برای مشتری و ارسال لینک پرداخت آنلاین (مرحلهٔ ۷).</summary>
    public const string OrdersCreate = "orders.create";

    public const string ProductsView = "products.view";
    public const string ProductsManage = "products.manage";
    public const string ProductsDelete = "products.delete";
    public const string ProductsSupplier = "products.supplier";
    public const string CatalogManage = "catalog.manage";
    public const string SuppliersManage = "suppliers.manage";

    public const string CommentsModerate = "comments.moderate";

    public const string CustomersView = "customers.view";
    public const string CustomersManage = "customers.manage";
    public const string CustomersWallet = "customers.wallet";

    public const string StaffManage = "staff.manage";

    public const string FinanceView = "finance.view";
    public const string FinanceExport = "finance.export";
    public const string WithdrawalsManage = "finance.withdrawals";

    /// <summary>گزارش‌های تحلیلی فاز ۷: قیف فروش، منبع ورودی/کمپین، نقشهٔ حرارتی، عملکرد محصول و تقاضا (بدون سود/قیمت خرید).</summary>
    public const string ReportsView = "reports.view";

    /// <summary>ثبت و ویرایش هزینه‌ها و هدف فروش ماهانه (فاز ۷ب).</summary>
    public const string ExpensesManage = "finance.expenses";

    public const string MarketingManage = "marketing.manage";
    public const string GiftCardsManage = "marketing.giftcards";
    public const string LoyaltyManage = "marketing.loyalty";
    public const string NotificationsSend = "notifications.send";

    public const string TicketsManage = "support.tickets";
    public const string MessagesManage = "support.messages";
    public const string ReturnsManage = "support.returns";

    public const string BlogWrite = "content.blog-write";
    public const string BlogPublish = "content.blog-publish";
    public const string ContentManage = "content.manage";

    public const string SeoManage = "seo.manage";

    public const string ShippingManage = "shipping.manage";
    public const string SettingsManage = "settings.manage";

    public const string MonitoringView = "system.monitoring";
    public const string AuditView = "system.audit";
    public const string SystemManage = "system.manage";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(Dashboard, "عمومی", "مشاهده داشبورد", "آمار کلی، کارهای در انتظار و نمودار فروش"),

        new(OrdersView, "سفارش‌ها", "مشاهده سفارش‌ها", "فهرست، جزئیات و چاپ فاکتور سفارش‌ها"),
        new(OrdersManage, "سفارش‌ها", "مدیریت سفارش‌ها", "تغییر وضعیت، کد رهگیری و یادداشت داخلی"),
        new(OrdersCancel, "سفارش‌ها", "لغو و مرجوع سفارش", "لغو سفارش و بازگشت وجه به کیف پول"),
        new(OrdersCreate, "سفارش‌ها", "ثبت سفارش برای مشتری", "ثبت سفارش از پنل و ارسال لینک پرداخت آنلاین به مشتری"),
        new(PurchaseInvoices, "سفارش‌ها", "فاکتورهای خرید", "بارگذاری و مشاهده فاکتور خرید از تأمین‌کننده"),

        new(ProductsView, "محصولات", "مشاهده محصولات"),
        new(ProductsManage, "محصولات", "افزودن و ویرایش محصول", "قیمت، موجودی، تصاویر، ویدیو، مشخصات و سئو"),
        new(ProductsDelete, "محصولات", "حذف محصول"),
        new(ProductsSupplier, "محصولات", "اطلاعات تأمین‌کننده و قیمت خرید", "لینک منبع خرید، تأمین‌کننده و قیمت تمام‌شده"),
        new(CatalogManage, "محصولات", "دسته‌بندی، برند و فیلترها"),
        new(SuppliersManage, "محصولات", "مدیریت تأمین‌کنندگان"),

        new(CommentsModerate, "نظرات", "مدیریت نظرات و پرسش‌ها", "تأیید، رد، حذف و پاسخ"),

        new(CustomersView, "مشتریان", "مشاهده مشتریان"),
        new(CustomersManage, "مشتریان", "مدیریت مشتریان", "ویرایش، مسدودسازی، خروج از همه دستگاه‌ها و امتیاز باشگاه"),
        new(CustomersWallet, "مشتریان", "اصلاح موجودی کیف پول"),

        new(StaffManage, "کارکنان", "مدیریت نقش‌ها و کارکنان", "ساخت نقش، تعیین دسترسی و انتساب نقش به کاربران"),

        new(FinanceView, "مالی", "گزارش‌های مالی", "فروش، سود، تخفیف‌ها، تعهدات کیف پول و کارت هدیه"),
        new(FinanceExport, "مالی", "خروجی اکسل و CSV"),
        new(WithdrawalsManage, "مالی", "تسویه درخواست‌های برداشت"),
        new(ExpensesManage, "مالی", "هزینه‌ها و هدف فروش", "ثبت هزینه‌های جاری (هاست، تبلیغات، حقوق …)، یادآوری سررسید و تعیین هدف فروش ماهانه"),

        new(ReportsView, "گزارش‌ها", "گزارش‌های تحلیلی", "قیف فروش، منبع ورودی و کمپین‌ها، ساعت‌های پربازدید، عملکرد محصولات و تقاضای مشتریان"),

        new(MarketingManage, "بازاریابی", "کوپن، بنر، اعلان صفحات و سبدهای رهاشده"),
        new(GiftCardsManage, "بازاریابی", "کارت‌های هدیه"),
        new(LoyaltyManage, "بازاریابی", "باشگاه مشتریان و دعوت از دوستان"),
        new(NotificationsSend, "بازاریابی", "ارسال اعلان و قالب پیام‌ها"),

        new(TicketsManage, "پشتیبانی", "تیکت‌های پشتیبانی"),
        new(MessagesManage, "پشتیبانی", "پیام‌های تماس با ما"),
        new(ReturnsManage, "پشتیبانی", "درخواست‌های مرجوعی و نظرسنجی خرید"),

        new(BlogWrite, "محتوا", "نوشتن مقاله", "ایجاد و ویرایش پیش‌نویس‌های خود و ارسال برای بررسی"),
        new(BlogPublish, "محتوا", "انتشار و سردبیری بلاگ", "انتشار، ویرایش همه مقاله‌ها و دسته‌های بلاگ"),
        new(ContentManage, "محتوا", "صفحات، منوها، صفحه اصلی و سؤالات متداول"),

        new(SeoManage, "سئو", "ابزارهای سئو", "سئوی صفحات، ریدایرکت‌ها، خطاهای ۴۰۴، ممیزی، مترادف‌ها و robots"),

        new(ShippingManage, "تنظیمات", "روش‌ها و تعرفه‌های ارسال"),
        new(SettingsManage, "تنظیمات", "تنظیمات فروشگاه"),

        new(MonitoringView, "فنی", "مانیتورینگ و سلامت سیستم", "خطاها، کاربران آنلاین، آمار درخواست‌ها، وضعیت سرویس‌ها"),
        new(AuditView, "فنی", "گزارش فعالیت کارکنان و رویدادهای امنیتی"),
        new(SystemManage, "فنی", "عملیات فنی", "پاک‌کردن کش، حل‌شدن خطاها و پاک‌سازی گزارش‌ها"),
    ];

    private static readonly HashSet<string> Keys = All.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string? key) => key is not null && Keys.Contains(key);

    /// <summary>Default roles created on first start (editable afterwards).</summary>
    public static readonly IReadOnlyList<(string Name, string Description, string Color, string[] Permissions)> DefaultRoles =
    [
        ("مدیر فروشگاه", "دسترسی کامل به همه بخش‌ها", "#7c3aed", All.Select(p => p.Key).ToArray()),
        ("مدیر فروش و سفارش‌ها", "پردازش سفارش‌ها، فاکتور خرید، مرجوعی و مشتریان", "#2563eb",
            [Dashboard, OrdersView, OrdersManage, OrdersCancel, OrdersCreate, PurchaseInvoices, ProductsView, ProductsSupplier, CustomersView, ReturnsManage, TicketsManage, CommentsModerate, ReportsView]),
        ("انبار و محصولات", "مدیریت کاتالوگ، موجودی، قیمت و تأمین‌کنندگان", "#0891b2",
            [Dashboard, ProductsView, ProductsManage, ProductsSupplier, CatalogManage, SuppliersManage, OrdersView, PurchaseInvoices]),
        ("پشتیبانی", "تیکت، پیام‌ها، نظرات و پیگیری سفارش", "#16a34a",
            [Dashboard, OrdersView, CustomersView, TicketsManage, MessagesManage, ReturnsManage, CommentsModerate]),
        ("حسابداری و مالی", "گزارش‌های مالی، خروجی‌ها و تسویه برداشت‌ها", "#ca8a04",
            [Dashboard, FinanceView, FinanceExport, WithdrawalsManage, OrdersView, PurchaseInvoices, CustomersView, CustomersWallet, GiftCardsManage, ReportsView, ExpensesManage]),
        ("بازاریابی", "کوپن، کارت هدیه، باشگاه، بنر و اعلان", "#db2777",
            [Dashboard, MarketingManage, GiftCardsManage, LoyaltyManage, NotificationsSend, ProductsView, CustomersView, ReportsView]),
        ("کارشناس سئو", "سئوی صفحات و محتوا، ریدایرکت‌ها و آمار جست‌وجو", "#059669",
            [Dashboard, SeoManage, ContentManage, BlogPublish, BlogWrite, ProductsView, CatalogManage]),
        ("نویسنده محتوا", "نوشتن مقاله و ارسال برای بررسی", "#9333ea", [BlogWrite]),
        ("سردبیر محتوا", "انتشار مقاله‌ها و مدیریت صفحات محتوایی", "#c026d3", [BlogWrite, BlogPublish, ContentManage]),
        ("پشتیبانی فنی", "مانیتورینگ، خطاها، گزارش فعالیت و عملیات فنی", "#475569",
            [Dashboard, MonitoringView, AuditView, SystemManage]),
    ];
}
