using Fratak_Shop.Core.DTOs.Admin;
using P = Fratak_Shop.Core.Services.Admin.AdminPermissions;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <param name="AnyOf">یکی از این مجوزها کافی است تا آیتم دیده شود.</param>
/// <param name="AllOf">مرحلهٔ ۱۴: علاوه بر AnyOf، همهٔ این مجوزها لازم است (صفحه‌ای که بدون هر دو کاری از آن برنمی‌آید؛
/// مثال: «درون‌ریزی از سایت» = مدیریت محصولات + اطلاعات تأمین‌کننده). بدون آن، نقش فروش لینکی می‌دید که به «دسترسی ندارید» می‌رسید.</param>
public sealed record AdminNavItem(string Title, string Url, string Icon, string[]? AnyOf = null, string? BadgeKey = null, bool OwnerOnly = false, string[]? AllOf = null)
{
    public bool IsVisible(AdminAccess access) => OwnerOnly
        ? access.IsOwner
        : (AnyOf is null || access.HasAny(AnyOf)) && (AllOf is null || AllOf.All(access.Has));
}

/// <summary>
/// گروه منو. دور ۲۳: در نوار کناری «تاشو» است (وضعیت باز/بسته در مرورگر کارمند ذخیره می‌شود؛ admin-nav.js) و گروهی که صفحهٔ فعلی
/// در آن است همیشه باز نمایش داده می‌شود. گروه بی‌عنوان (داشبورد و موارد شخصی) همیشه باز است.
/// </summary>
public sealed record AdminNavGroup(string Title, params AdminNavItem[] Items);

/// <summary>Menu of the admin panel. Items are shown only to staff with one of the listed permissions.</summary>
public static class AdminNav
{
    public static readonly AdminNavGroup[] Groups =
    [
        new("", new AdminNavItem("داشبورد", "/admin", "SquaresFour"), new AdminNavItem("اعلان‌های من", "/admin/inbox", "Tray"),
            new AdminNavItem("میانبرهای من", "/admin/ux", "PushPin"),
            // مرحلهٔ هفتم – فاز ۱۰: کار تیمی و امنیت کارکنان.
            new AdminNavItem("کارهای من", "/admin/tasks", "ListChecks", null, "tasks"),
            new AdminNavItem("تأییدهای دونفره", "/admin/approvals", "ShieldCheck", [P.CustomersWallet, P.WithdrawalsManage, P.StaffManage], "approvals"),
            new AdminNavItem("امنیت حساب من", "/admin/account", "Fingerprint")),
        new("فروش",
            new AdminNavItem("سفارش‌ها", "/admin/orders", "Orders", [P.OrdersView], "orders"),
            new AdminNavItem("کانبان سفارش‌ها", "/admin/orders/board", "Kanban", [P.OrdersView]),
            new AdminNavItem("رهگیری مرسوله‌ها", "/admin/orders/shipments", "Truck", [P.OrdersView]),
            new AdminNavItem("ثبت سفارش برای مشتری", "/admin/orders/new", "Plus", [P.OrdersCreate]),
            new AdminNavItem("فاکتورهای خرید", "/admin/orders/purchase-invoices", "Receipt", [P.PurchaseInvoices, P.FinanceView]),
            new AdminNavItem("مرجوعی‌ها", "/admin/returns", "ArrowUUpLeft", [P.ReturnsManage], "returns"),
            new AdminNavItem("مشتریان", "/admin/customers", "Users", [P.CustomersView]),
            new AdminNavItem("نظرسنجی خرید", "/admin/feedback", "Smile", [P.OrdersView, P.TicketsManage])),
        new("کاتالوگ",
            new AdminNavItem("محصولات", "/admin/products", "Package", [P.ProductsView]),
            new AdminNavItem("بررسی تأمین‌کننده", "/admin/products/supplier-sync", "ArrowsClockwise", [P.ProductsSupplier], "supplier"),
            new AdminNavItem("انبار و موجودی", "/admin/products/inventory", "ChartLine", [P.ProductsView]),
            new AdminNavItem("رصد قیمت رقبا", "/admin/products/competitors", "Tag", [P.ProductsView]),
            new AdminNavItem("تغییر گروهی قیمت", "/admin/products/bulk-price", "Calculator", [P.ProductsManage]),
            new AdminNavItem("ورود از اکسل", "/admin/products/import", "UploadSimple", [P.ProductsManage]),
            new AdminNavItem("درون‌ریزی از سایت", "/admin/products/site-import", "Storefront", [P.ProductsManage], AllOf: [P.ProductsManage, P.ProductsSupplier]),
            new AdminNavItem("دسته‌بندی‌ها", "/admin/manage/categories", "Stack", [P.CatalogManage]),
            new AdminNavItem("برندها", "/admin/manage/brands", "Tag", [P.CatalogManage]),
            new AdminNavItem("تأمین‌کنندگان", "/admin/manage/suppliers", "Factory", [P.SuppliersManage]),
            new AdminNavItem("ویدیوهای محصول", "/admin/manage/product-videos", "VideoCamera", [P.ProductsManage]),
            new AdminNavItem("نظرات و پرسش‌ها", "/admin/comments", "ChatDots", [P.CommentsModerate], "comments")),
        // دور ۲۳: همهٔ صفحه‌های هوشمند یک‌جا (قبلاً بین کاتالوگ، گزارش‌ها، محتوا و تنظیمات پخش بودند).
        new("هوشمند",
            new AdminNavItem("محتوای هوشمند", "/admin/smart", "MagicWand", [P.ProductsManage, P.CatalogManage, P.BlogPublish]),
            new AdminNavItem("خلاصهٔ روزانه", "/admin/smart/brief", "ListChecks", [P.ReportsView, P.ProductsManage]),
            new AdminNavItem("دسته‌بندی و درخت دسته‌ها", "/admin/smart/categories", "Stack", [P.CatalogManage]),
            new AdminNavItem("تقویم بلاگ خودکار", "/admin/smart/blog", "Sparkle", [P.BlogPublish]),
            new AdminNavItem("رشد خودکار", "/admin/smart/growth", "TrendUp", [P.ReportsView, P.MarketingManage, P.SeoManage]),
            new AdminNavItem("دستیار فروشگاه", "/admin/smart/assistant", "Robot", [P.ProductsManage, P.ReportsView, P.MarketingManage, P.TicketsManage]),
            new AdminNavItem("تنظیمات هوشمند", "/admin/smart/settings", "Gear", [P.SettingsManage])),
        new("محتوا و سئو",
            new AdminNavItem("مقاله‌ها", "/admin/blog", "Article", [P.BlogWrite, P.BlogPublish], "drafts"),
            new AdminNavItem("صفحات و محتوا", "/admin/content", "Notebook", [P.ContentManage]),
            new AdminNavItem("کتابخانهٔ رسانه", "/admin/media", "Images", [P.ContentManage, P.BlogWrite, P.ProductsManage, P.MarketingManage]),
            new AdminNavItem("سئو", "/admin/seo", "Globe", [P.SeoManage], "seo"),
            // دور سئوی خودکار: داشبورد، کارها، تاریخچه (برگشت) و ربات‌ها/سرعت؛ مشاور سئو (SeoView) فقط می‌بیند.
            new AdminNavItem("سئوی خودکار", "/admin/seo/autopilot", "Gauge", [P.SeoView, P.SeoManage]),
            new AdminNavItem("کارهای سئو", "/admin/seo/tasks", "Warning", [P.SeoView, P.SeoManage]),
            new AdminNavItem("تاریخچهٔ سئو", "/admin/seo/history", "Clock", [P.SeoView, P.SeoManage]),
            new AdminNavItem("ربات‌ها و سرعت", "/admin/seo/signals", "Monitor", [P.SeoView, P.SeoManage]),
            new AdminNavItem("موتور هوشمند سئو", "/admin/seo/engine", "MagicWand", [P.SeoManage]),
            new AdminNavItem("بررسی پیشنهادهای سئو", "/admin/seo/review", "ListChecks", [P.SeoManage], "seo-review"),
            new AdminNavItem("ویرایش گروهی سئو", "/admin/seo/bulk", "PencilSimple", [P.SeoManage]),
            // مرحلهٔ شانزدهم: جدول دانش سئو و ویرایشگر هوشمند متن (همان مجوزهای صفحه).
            new AdminNavItem("جدول دانش سئو", "/admin/seo/knowledge", "Database", [P.SeoManage]),
            new AdminNavItem("ویرایشگر هوشمند متن", "/admin/seo/smart", "Sparkle", [P.ProductsManage, P.CatalogManage, P.BlogWrite, P.ContentManage, P.SeoManage]),
            new AdminNavItem("لینک‌های شکسته", "/admin/seo/links", "LinkBreak", [P.SeoManage]),
            new AdminNavItem("Search Console", "/admin/seo/search-console", "ChartLine", [P.SeoManage])),
        new("بازاریابی",
            new AdminNavItem("کوپن‌ها", "/admin/manage/coupons", "Percent", [P.MarketingManage]),
            new AdminNavItem("کارت‌های هدیه", "/admin/gift-cards", "Gift", [P.GiftCardsManage]),
            new AdminNavItem("باشگاه مشتریان", "/admin/manage/loyalty-rewards", "Crown", [P.LoyaltyManage]),
            new AdminNavItem("بنرها", "/admin/manage/banners", "Image", [P.MarketingManage, P.ContentManage]),
            // دور ۲۱: استودیوی طراحی (بنر، کاور مقاله، پست) و صفحه‌ساز کمپین.
            new AdminNavItem("استودیوی طراحی", "/admin/design", "Palette", [P.MarketingManage, P.ContentManage, P.BlogPublish]),
            new AdminNavItem("صفحه‌های کمپین", "/admin/campaigns", "Notebook", [P.MarketingManage, P.ContentManage]),
            new AdminNavItem("ارسال اعلان", "/admin/notifications", "Megaphone", [P.NotificationsSend]),
            new AdminNavItem("قالب‌های پیام رویدادها", "/admin/notifications/templates", "Bell", [P.NotificationsSend, P.SettingsManage]),
            new AdminNavItem("پیام‌های آماده", "/admin/ready-messages", "ChatCenteredText", [P.NotificationsSend, P.TicketsManage, P.CommentsModerate, P.MessagesManage, P.OrdersManage]),
            new AdminNavItem("سبدهای رهاشده", "/admin/abandoned-carts", "CartFull", [P.MarketingManage])),
        new("پشتیبانی",
            new AdminNavItem("تیکت‌ها", "/admin/tickets", "Lifebuoy", [P.TicketsManage], "tickets"),
            new AdminNavItem("پیام‌های تماس", "/admin/messages", "Envelope", [P.MessagesManage], "messages")),
        new("مالی",
            new AdminNavItem("گزارش‌های مالی", "/admin/finance", "ChartLine", [P.FinanceView]),
            new AdminNavItem("تراکنش‌های درگاه", "/admin/finance/payments", "CreditCard", [P.FinanceView]),
            new AdminNavItem("درخواست‌های برداشت", "/admin/finance/withdrawals", "Bank", [P.WithdrawalsManage, P.FinanceView], "withdrawals"),
            new AdminNavItem("تراکنش‌های کیف پول", "/admin/finance/wallet", "Wallet", [P.FinanceView, P.CustomersWallet]),
            new AdminNavItem("هزینه‌ها و سود خالص", "/admin/finance/expenses", "Coins", [P.FinanceView, P.ExpensesManage])),
        // مرحلهٔ هفتم – فاز ۷: گزارش‌های تحلیلی (آمار بازدید داخلی).
        new("گزارش‌ها",
            new AdminNavItem("قیف فروش و بازدید", "/admin/reports", "Funnel", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("عملکرد محصولات", "/admin/reports/products", "TrendUp", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("تقاضای مشتریان", "/admin/reports/demand", "MagnifyingGlass", [P.ReportsView, P.FinanceView, P.MarketingManage, P.ProductsManage]),
            new AdminNavItem("منبع ورودی و کمپین‌ها", "/admin/reports/campaigns", "Target", [P.ReportsView, P.FinanceView, P.MarketingManage]),
            new AdminNavItem("هدف فروش ماهانه", "/admin/reports/goals", "Trophy", [P.ReportsView, P.FinanceView, P.ExpensesManage]),
            new AdminNavItem("عملکرد کارکنان", "/admin/reports/staff", "UsersThree", [P.StaffManage, P.AuditView]),
            new AdminNavItem("گزارش‌های خودکار", "/admin/reports/scheduled", "PaperPlaneTilt", [P.FinanceView, P.SettingsManage])),
        new("تنظیمات",
            new AdminNavItem("تنظیمات فروشگاه", "/admin/settings", "Gear", [P.SettingsManage]),
            // مرحلهٔ دوازدهم: لوگوی همهٔ محل‌ها + آیکون اپ.
            new AdminNavItem("استودیوی لوگو", "/admin/settings/brand", "Palette", [P.SettingsManage]),
            new AdminNavItem("ارسال و تحویل", "/admin/manage/shipping-methods", "Truck", [P.ShippingManage]),
            new AdminNavItem("کارکنان و نقش‌ها", "/admin/staff", "UserGear", [P.StaffManage]),
            new AdminNavItem("همه جدول‌ها", "/admin/manage", "Sliders"),
            new AdminNavItem("سطل بازیافت", "/admin/trash", "Trash", [P.ProductsDelete, P.BlogPublish, P.CatalogManage, P.ContentManage, P.LoyaltyManage,
                P.MarketingManage, P.ProductsManage, P.ReturnsManage, P.SeoManage, P.ShippingManage, P.SuppliersManage, P.TicketsManage, P.ExpensesManage]),
            new AdminNavItem("راهنمای صفحه‌ها", "/admin/help", "Question", [P.SettingsManage])),
        new("فنی",
            new AdminNavItem("سلامت سامانه", "/admin/system", "Pulse", [P.MonitoringView]),
            new AdminNavItem("کاربران آنلاین", "/admin/system/live", "Monitor", [P.MonitoringView]),
            // مرحلهٔ هفدهم: علت هر ۴۰۳/۴۲۹/۵۰۳/«دسترسی ندارید» و تاریخچهٔ روشن/خاموش شدن برنامه.
            new AdminNavItem("درخواست‌های ردشده", "/admin/system/access", "Prohibit", [P.MonitoringView]),
            new AdminNavItem("خطاها", "/admin/system/errors", "Bug", [P.MonitoringView], "errors"),
            new AdminNavItem("گزارش فعالیت", "/admin/system/audit", "ClockCounterClockwise", [P.AuditView]),
            new AdminNavItem("رویدادهای امنیتی", "/admin/system/security", "ShieldCheck", [P.AuditView, P.MonitoringView]),
            // مرحلهٔ هفتم – فاز ۹: ابزارهای فنی.
            new AdminNavItem("پشتیبان‌گیری", "/admin/system/backups", "Database", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("کارهای پس‌زمینه", "/admin/system/jobs", "ArrowsClockwise", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("منابع سرور", "/admin/system/resources", "Gauge", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("فایروال", "/admin/system/firewall", "Prohibit", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("هشدارها", "/admin/system/alerts", "BellRinging", [P.MonitoringView, P.SystemManage]),
            // دور هشتم: تنظیم و آزمون سرویس پیامک (SMS.ir).
            new AdminNavItem("سرویس پیامک", "/admin/system/sms", "ChatDots", [P.MonitoringView, P.SystemManage]),
            // مرحلهٔ دوازدهم: تنظیم و آزمون سرویس ایمیل (SMTP).
            new AdminNavItem("سرویس ایمیل", "/admin/system/email", "Envelope", [P.MonitoringView, P.SystemManage]),
            new AdminNavItem("گزارش‌های سرور", "/admin/system/logs", "Article", [P.MonitoringView])),
    ];
}
