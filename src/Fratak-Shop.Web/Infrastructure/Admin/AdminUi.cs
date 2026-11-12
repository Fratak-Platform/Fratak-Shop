using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.WebUtilities;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>Small view helpers of the admin panel (badges, query-string links, sizes).</summary>
public static class AdminUi
{
    public static string OrderStatus(OrderStatus status) => AdminOrderService.OrderStatusTitle(status);

    public static string OrderStatusBadge(OrderStatus status) => status switch
    {
        Data.Entities.Orders.OrderStatus.Pending => "a-badge-yellow",
        Data.Entities.Orders.OrderStatus.Confirmed => "a-badge-blue",
        Data.Entities.Orders.OrderStatus.Processing => "a-badge-purple",
        Data.Entities.Orders.OrderStatus.Shipped => "a-badge-sky",
        Data.Entities.Orders.OrderStatus.Delivered => "a-badge-green",
        _ => "a-badge-red",
    };

    /// <summary>رنگ نشان وضعیت رهگیری مرسوله (مرحلهٔ ۷ – فاز ۶).</summary>
    public static string ShipmentStateBadge(ShipmentTrackingState state) => state switch
    {
        ShipmentTrackingState.Delivered => "a-badge-green",
        ShipmentTrackingState.OutForDelivery => "a-badge-purple",
        ShipmentTrackingState.InTransit => "a-badge-sky",
        ShipmentTrackingState.Returned => "a-badge-red",
        ShipmentTrackingState.NotFound => "a-badge-yellow",
        ShipmentTrackingState.Pending => "a-badge-blue",
        _ => "a-badge-gray",
    };

    public static string PaymentStatusBadge(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => "a-badge-green",
        PaymentStatus.Failed => "a-badge-red",
        PaymentStatus.Refunded => "a-badge-gray",
        _ => "a-badge-yellow",
    };

    public static string TransactionStatus(PaymentTransactionStatus status) => status switch
    {
        PaymentTransactionStatus.Pending => "در انتظار",
        PaymentTransactionStatus.Verifying => "در حال تأیید",
        PaymentTransactionStatus.Succeeded => "موفق",
        PaymentTransactionStatus.Failed => "ناموفق",
        PaymentTransactionStatus.Cancelled => "انصراف",
        _ => "نامشخص",
    };

    public static string TransactionBadge(PaymentTransactionStatus status) => status switch
    {
        PaymentTransactionStatus.Succeeded => "a-badge-green",
        PaymentTransactionStatus.Failed => "a-badge-red",
        PaymentTransactionStatus.Cancelled => "a-badge-gray",
        _ => "a-badge-yellow",
    };

    public static string PaymentPurpose(PaymentPurpose purpose) => purpose switch
    {
        Data.Entities.Payments.PaymentPurpose.OrderPayment => "پرداخت سفارش",
        Data.Entities.Payments.PaymentPurpose.WalletTopUp => "شارژ کیف پول",
        Data.Entities.Payments.PaymentPurpose.GiftCard => "خرید کارت هدیه",
        _ => "نامشخص",
    };

    public static string SecurityEvent(Data.Entities.Admin.SecurityEventType type) => type switch
    {
        Data.Entities.Admin.SecurityEventType.LoginSucceeded => "ورود موفق",
        Data.Entities.Admin.SecurityEventType.LoginFailed => "ورود ناموفق",
        Data.Entities.Admin.SecurityEventType.LockedOut => "قفل شدن حساب",
        Data.Entities.Admin.SecurityEventType.OtpFailed => "کد یکبارمصرف نادرست",
        Data.Entities.Admin.SecurityEventType.BlockedUserLogin => "تلاش ورود حساب مسدود",
        Data.Entities.Admin.SecurityEventType.AdminAccessDenied => "دسترسی غیرمجاز به پنل",
        Data.Entities.Admin.SecurityEventType.UserBlocked => "مسدودسازی کاربر",
        Data.Entities.Admin.SecurityEventType.UserUnblocked => "رفع مسدودی",
        Data.Entities.Admin.SecurityEventType.RolesChanged => "تغییر نقش‌ها",
        Data.Entities.Admin.SecurityEventType.SessionsRevoked => "خروج اجباری از دستگاه‌ها",
        Data.Entities.Admin.SecurityEventType.TotpEnabled => "فعال‌سازی برنامهٔ احراز هویت",
        Data.Entities.Admin.SecurityEventType.TotpDisabled => "غیرفعال‌سازی برنامهٔ احراز هویت",
        Data.Entities.Admin.SecurityEventType.TotpFailed => "کد نادرست برنامهٔ احراز هویت",
        Data.Entities.Admin.SecurityEventType.ReauthFailed => "تأیید دوبارهٔ ناموفق",
        Data.Entities.Admin.SecurityEventType.TotpRecoveryUsed => "استفاده از کد بازیابی",
        Data.Entities.Admin.SecurityEventType.PasswordReset => "بازیابی رمز عبور با کد پیامکی",
        _ => type.ToString(),
    };

    public static string SecurityEventBadge(Data.Entities.Admin.SecurityEventType type) => type switch
    {
        Data.Entities.Admin.SecurityEventType.LoginSucceeded or Data.Entities.Admin.SecurityEventType.UserUnblocked => "a-badge-green",
        Data.Entities.Admin.SecurityEventType.LoginFailed or Data.Entities.Admin.SecurityEventType.OtpFailed
            or Data.Entities.Admin.SecurityEventType.TotpFailed or Data.Entities.Admin.SecurityEventType.ReauthFailed
            or Data.Entities.Admin.SecurityEventType.TotpRecoveryUsed => "a-badge-yellow",
        Data.Entities.Admin.SecurityEventType.RolesChanged or Data.Entities.Admin.SecurityEventType.SessionsRevoked
            or Data.Entities.Admin.SecurityEventType.TotpEnabled or Data.Entities.Admin.SecurityEventType.PasswordReset => "a-badge-blue",
        _ => "a-badge-red",
    };

    public static string Bool(bool value) => value ? "a-badge-green" : "a-badge-gray";

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} بایت",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} کیلوبایت",
        _ => $"{bytes / 1024d / 1024d:0.#} مگابایت",
    };

    public static string Duration(TimeSpan span) => span.TotalDays >= 1
        ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت"
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه" : $"{Math.Max(0, (int)span.TotalMinutes)} دقیقه";

    public static string Ago(DateTimeOffset moment)
    {
        var span = DateTimeOffset.UtcNow - moment;
        if (span.TotalSeconds < 60) return "لحظاتی پیش";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} دقیقه پیش";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} ساعت پیش";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} روز پیش";
        return Helpers.Format.Date(moment);
    }

    /// <summary>Current URL with some query values replaced (null removes the key) – filters, tabs and paging links.</summary>
    public static string With(HttpRequest request, params (string Key, string? Value)[] values)
    {
        var query = QueryHelpers.ParseQuery(request.QueryString.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrEmpty(value))
                query.Remove(key);
            else
                query[key] = value;
        }

        if (!values.Any(v => string.Equals(v.Key, "page", StringComparison.OrdinalIgnoreCase)))
            query.Remove("page");

        return QueryHelpers.AddQueryString(request.Path, query.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    }

    public static IHtmlContent Money(decimal amount, bool withUnit = true) =>
        new HtmlString($"<span class=\"num\">{Helpers.Format.Price(amount)}</span>{(withUnit ? " <small class=\"a-muted\">تومان</small>" : "")}");

    /// <summary>Percentage change between two values, e.g. "+12٪".</summary>
    public static (string Text, bool Up)? Change(decimal current, decimal previous)
    {
        if (previous == 0)
            return null;
        var percent = Math.Round((current - previous) * 100 / previous, 0);
        return ($"{(percent > 0 ? "+" : "")}{percent}٪", percent >= 0);
    }
}

public sealed record PagerModel(int Page, int PageCount, int Total);

public sealed record BarChartModel(string Title, IReadOnlyList<Core.DTOs.Admin.ChartPoint> Points, bool Money = true, string? SecondaryLabel = null);

public sealed record HBarsModel(IReadOnlyList<Core.DTOs.Admin.NamedAmount> Rows, bool ByCount = false, bool ShowCount = true);
