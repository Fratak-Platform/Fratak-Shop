using Fratak_Shop.Core.DTOs.Admin;
using P = Fratak_Shop.Core.Services.Admin.AdminPermissions;

namespace Fratak_Shop.Web.Infrastructure.Admin;

public sealed record AdminNavItem(string Title, string Url, string Icon, string[]? AnyOf = null, string? BadgeKey = null, bool OwnerOnly = false)
{
    public bool IsVisible(AdminAccess access) => OwnerOnly ? access.IsOwner : AnyOf is null || access.HasAny(AnyOf);
}

public sealed record AdminNavGroup(string Title, params AdminNavItem[] Items);

/// <summary>Menu of the admin panel. Items are shown only to staff with one of the listed permissions.</summary>
public static class AdminNav
{
    public static readonly AdminNavGroup[] Groups =
    [
        new("", new AdminNavItem("داشبورد", "/admin", "SquaresFour"), new AdminNavItem("اعلان‌های من", "/admin/inbox", "Tray"),
            new AdminNavItem("میانبرهای من", "/admin/ux", "PushPin")),
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
            new AdminNavItem("تغییر گروهی قیمت", "/admin/products/bulk-price", "Calculator", [P.ProductsManage]),
            new AdminNavItem("ورود از اکسل", "/admin/products/import", "UploadSimple", [P.ProductsManage]),
            new AdminNavItem("دسته‌بندی‌ها", "/admin/manage/categories", "Stack", [P.CatalogManage]),
            new AdminNavItem("برندها", "/admin/manage/brands", "Tag", [P.CatalogManage]),
            new AdminNavItem("تأمین‌کنندگان", "/admin/manage/suppliers", "Factory", [P.SuppliersManage]),
            new AdminNavItem("ویدیوهای محصول", "/admin/manage/product-videos", "VideoCamera", [P.ProductsManage]),
            new AdminNavItem("نظرات و پرسش‌ها", "/admin/comments", "ChatDots", [P.CommentsModerate], "comments")),
        new("پشتیبانی",
            new AdminNavItem("تیکت‌ها", "/admin/tickets", "Lifebuoy", [P.TicketsManage], "tickets"),
            new AdminNavItem("پیام‌های تماس", "/admin/messages", "Envelope", [P.MessagesManage], "messages")),
        new("مالی",
            new AdminNavItem("گزارش‌های مالی", "/admin/finance", "ChartLine", [P.FinanceView]),
            new AdminNavItem("تراکنش‌های درگاه", "/admin/finance/payments", "CreditCard", [P.FinanceView]),
            new AdminNavItem("درخواست‌های برداشت", "/admin/finance/withdrawals", "Bank", [P.WithdrawalsManage, P.FinanceView], "withdrawals"),
            new AdminNavItem("تراکنش‌های کیف پول", "/admin/finance/wallet", "Wallet", [P.FinanceView, P.CustomersWallet])),
        new("بازاریابی",
            new AdminNavItem("کوپن‌ها", "/admin/manage/coupons", "Percent", [P.MarketingManage]),
            new AdminNavItem("کارت‌های هدیه", "/admin/gift-cards", "Gift", [P.GiftCardsManage]),
            new AdminNavItem("باشگاه مشتریان", "/admin/manage/loyalty-rewards", "Crown", [P.LoyaltyManage]),
            new AdminNavItem("بنرها", "/admin/manage/banners", "Image", [P.MarketingManage, P.ContentManage]),
            new AdminNavItem("ارسال اعلان", "/admin/notifications", "Megaphone", [P.NotificationsSend]),
            new AdminNavItem("قالب‌های پیام رویدادها", "/admin/notifications/templates", "Bell", [P.NotificationsSend, P.SettingsManage]),
            new AdminNavItem("پیام‌های آماده", "/admin/ready-messages", "ChatCenteredText", [P.NotificationsSend, P.TicketsManage, P.CommentsModerate, P.MessagesManage, P.OrdersManage]),
            new AdminNavItem("سبدهای رهاشده", "/admin/abandoned-carts", "CartFull", [P.MarketingManage])),
        new("محتوا و سئو",
            new AdminNavItem("مقاله‌ها", "/admin/blog", "Article", [P.BlogWrite, P.BlogPublish], "drafts"),
            new AdminNavItem("صفحات و محتوا", "/admin/content", "Notebook", [P.ContentManage]),
            new AdminNavItem("کتابخانهٔ رسانه", "/admin/media", "Images", [P.ContentManage, P.BlogWrite, P.ProductsManage, P.MarketingManage]),
            new AdminNavItem("سئو", "/admin/seo", "Globe", [P.SeoManage], "seo")),
        new("تنظیمات",
            new AdminNavItem("تنظیمات فروشگاه", "/admin/settings", "Gear", [P.SettingsManage]),
            new AdminNavItem("ارسال و تحویل", "/admin/manage/shipping-methods", "Truck", [P.ShippingManage]),
            new AdminNavItem("کارکنان و نقش‌ها", "/admin/staff", "UserGear", [P.StaffManage]),
            new AdminNavItem("همه جدول‌ها", "/admin/manage", "Sliders"),
            new AdminNavItem("سطل بازیافت", "/admin/trash", "Trash", [P.ProductsDelete, P.BlogPublish, P.CatalogManage, P.ContentManage, P.LoyaltyManage,
                P.MarketingManage, P.ProductsManage, P.ReturnsManage, P.SeoManage, P.ShippingManage, P.SuppliersManage, P.TicketsManage]),
            new AdminNavItem("راهنمای صفحه‌ها", "/admin/help", "Question", [P.SettingsManage])),
        new("فنی",
            new AdminNavItem("سلامت سامانه", "/admin/system", "Pulse", [P.MonitoringView]),
            new AdminNavItem("کاربران آنلاین", "/admin/system/live", "Monitor", [P.MonitoringView]),
            new AdminNavItem("خطاها", "/admin/system/errors", "Bug", [P.MonitoringView], "errors"),
            new AdminNavItem("گزارش فعالیت", "/admin/system/audit", "ClockCounterClockwise", [P.AuditView]),
            new AdminNavItem("رویدادهای امنیتی", "/admin/system/security", "ShieldCheck", [P.AuditView, P.MonitoringView])),
    ];
}
