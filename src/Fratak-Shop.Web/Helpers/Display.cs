using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Web.Helpers;

/// <summary>
/// Persian labels and badge styles for domain enums.
/// </summary>
public static class Display
{
    public static string OrderStatus(OrderStatus status) => status switch
    {
        Data.Entities.Orders.OrderStatus.Pending => "در انتظار تایید",
        Data.Entities.Orders.OrderStatus.Confirmed => "تایید شده",
        Data.Entities.Orders.OrderStatus.Processing => "در حال پردازش",
        Data.Entities.Orders.OrderStatus.Shipped => "ارسال شده",
        Data.Entities.Orders.OrderStatus.Delivered => "تحویل داده شده",
        Data.Entities.Orders.OrderStatus.Cancelled => "لغو شده",
        Data.Entities.Orders.OrderStatus.Returned => "مرجوع شده",
        _ => "نامشخص",
    };

    public static string OrderStatusBadge(OrderStatus status) => status switch
    {
        Data.Entities.Orders.OrderStatus.Delivered => "bg-green-100 text-green-700",
        Data.Entities.Orders.OrderStatus.Shipped => "bg-sky-100 text-sky-700",
        Data.Entities.Orders.OrderStatus.Confirmed or Data.Entities.Orders.OrderStatus.Processing => "bg-blue-100 text-blue-700",
        Data.Entities.Orders.OrderStatus.Cancelled or Data.Entities.Orders.OrderStatus.Returned => "bg-red-100 text-red-600",
        _ => "bg-yellow-100 text-yellow-700",
    };

    public static string PaymentMethod(PaymentMethod method) => method switch
    {
        Data.Entities.Orders.PaymentMethod.OnlineGateway => "پرداخت اینترنتی",
        Data.Entities.Orders.PaymentMethod.CashOnDelivery => "پرداخت در محل",
        Data.Entities.Orders.PaymentMethod.BankTransfer => "کارت به کارت",
        Data.Entities.Orders.PaymentMethod.Wallet => "کیف پول",
        _ => "نامشخص",
    };

    public static string PaymentStatus(PaymentStatus status) => status switch
    {
        Data.Entities.Orders.PaymentStatus.Pending => "در انتظار پرداخت",
        Data.Entities.Orders.PaymentStatus.Paid => "پرداخت شده",
        Data.Entities.Orders.PaymentStatus.Failed => "ناموفق",
        Data.Entities.Orders.PaymentStatus.Refunded => "بازگشت داده شده",
        _ => "نامشخص",
    };

    public static string PaymentStatusBadge(PaymentStatus status) => status switch
    {
        Data.Entities.Orders.PaymentStatus.Paid => "bg-green-100 text-green-700",
        Data.Entities.Orders.PaymentStatus.Failed => "bg-red-100 text-red-600",
        Data.Entities.Orders.PaymentStatus.Refunded => "bg-zinc-100 text-zinc-600",
        _ => "bg-yellow-100 text-yellow-700",
    };

    public static string MessageType(MessageType type) => type switch
    {
        Data.Entities.Messages.MessageType.Contact => "تماس با ما",
        Data.Entities.Messages.MessageType.Support => "پشتیبانی",
        Data.Entities.Messages.MessageType.Suggestion => "پیشنهاد",
        Data.Entities.Messages.MessageType.Complaint => "شکایت",
        _ => "پیام",
    };

    public static string MessageStatus(MessageStatus status) => status switch
    {
        Data.Entities.Messages.MessageStatus.New => "ثبت شده",
        Data.Entities.Messages.MessageStatus.InProgress => "در حال بررسی",
        Data.Entities.Messages.MessageStatus.Answered => "پاسخ داده شده",
        Data.Entities.Messages.MessageStatus.Closed => "بسته شده",
        _ => "نامشخص",
    };

    public static string MessageStatusBadge(MessageStatus status) => status switch
    {
        Data.Entities.Messages.MessageStatus.Answered => "bg-green-500",
        Data.Entities.Messages.MessageStatus.InProgress => "bg-sky-500",
        Data.Entities.Messages.MessageStatus.Closed => "bg-zinc-500",
        _ => "bg-yellow-500",
    };

    public static string WalletTransactionType(WalletTransactionType type) => type switch
    {
        Data.Entities.Wallets.WalletTransactionType.TopUp => "شارژ کیف پول",
        Data.Entities.Wallets.WalletTransactionType.OrderPayment => "پرداخت سفارش",
        Data.Entities.Wallets.WalletTransactionType.OrderRefund => "بازگشت وجه سفارش",
        Data.Entities.Wallets.WalletTransactionType.Withdrawal => "برداشت",
        Data.Entities.Wallets.WalletTransactionType.WithdrawalReversal => "برگشت درخواست برداشت",
        Data.Entities.Wallets.WalletTransactionType.AdminCredit => "افزایش اعتبار",
        Data.Entities.Wallets.WalletTransactionType.AdminDebit => "کسر اعتبار",
        _ => "تراکنش",
    };

    public static string WithdrawalStatus(WithdrawalStatus status) => status switch
    {
        Data.Entities.Wallets.WithdrawalStatus.Pending => "در انتظار بررسی",
        Data.Entities.Wallets.WithdrawalStatus.Approved => "تایید شده",
        Data.Entities.Wallets.WithdrawalStatus.Paid => "واریز شده",
        Data.Entities.Wallets.WithdrawalStatus.Rejected => "رد شده",
        Data.Entities.Wallets.WithdrawalStatus.Cancelled => "لغو شده",
        _ => "نامشخص",
    };

    public static string WithdrawalStatusBadge(WithdrawalStatus status) => status switch
    {
        Data.Entities.Wallets.WithdrawalStatus.Paid => "bg-green-100 text-green-700",
        Data.Entities.Wallets.WithdrawalStatus.Approved => "bg-sky-100 text-sky-700",
        Data.Entities.Wallets.WithdrawalStatus.Rejected or Data.Entities.Wallets.WithdrawalStatus.Cancelled => "bg-red-100 text-red-600",
        _ => "bg-yellow-100 text-yellow-700",
    };

    /// <summary>Short device description from a user agent ("Chrome روی Windows").</summary>
    public static string Device(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "دستگاه نامشخص";

        var ua = userAgent;
        var os = ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS"
            : ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : null;
        var browser = ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : null;

        return (browser, os) switch
        {
            (null, null) => "مرورگر / برنامه دیگر",
            (null, var o) => o!,
            (var b, null) => b!,
            var (b, o) => $"{b} روی {o}",
        };
    }
}
