using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// صفحهٔ عمومی «لینک پرداخت» سفارشی که کارمند در پنل ثبت کرده است (<c>/pay/{token}</c>).
/// <para>
/// ورود لازم نیست: توکن ۳۲ نویسه‌ای تصادفی خودش مجوز دیدن همین یک سفارش و پرداخت آن است (مثل لینک پرداخت
/// درگاه‌ها). مبلغ همیشه از پایگاه داده خوانده می‌شود و ثبت پرداخت فقط با تأیید زرین‌پال در Callback انجام می‌شود.
/// بعد از بازگشت از درگاه، <c>Payment/Callback</c> مشتری را دوباره به همین صفحه برمی‌گرداند تا نتیجه را ببیند.
/// </para>
/// </summary>
public class PayModel(IStaffOrderService staffOrders, IOrderService orderService, IPaymentFlowService paymentFlow, IMemoryCache cache) : ShopPageModel
{
    /// <summary>سقف شروع پرداخت برای هر لینک در یک ساعت (جلوگیری از ساختن تراکنش‌های بی‌شمار با یک لینک).</summary>
    private const int MaxStartsPerHour = 10;

    public PaymentLinkView View { get; private set; } = null!;

    public string Token { get; private set; } = null!;

    /// <summary>این سفارش مال کاربر واردشده است (لینک «جزئیات سفارش» در پروفایل نمایش داده می‌شود).</summary>
    public bool IsOwner { get; private set; }

    public async Task<IActionResult> OnGetAsync(string token)
    {
        if (!await LoadAsync(token))
            return NotFound();

        ViewData[PageMetaKeys.Title] = $"پرداخت سفارش {View.OrderNumber}";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        // لینک پرداخت نباید در کش مرورگر/پراکسی یا هدر Referer سایت‌های دیگر جا بماند.
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string token)
    {
        if (!await LoadAsync(token))
            return NotFound();

        if (View.IsPaid)
            return RedirectToPage(new { token });
        if (!View.IsPayable || View.UserId is null)
        {
            ErrorMessage = "مهلت پرداخت این سفارش به پایان رسیده یا سفارش لغو شده است.";
            return RedirectToPage(new { token });
        }

        var key = "pay-starts:" + token;
        var starts = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new int[1]; })!;
        if (Interlocked.Increment(ref starts[0]) > MaxStartsPerHour)
        {
            ErrorMessage = "تعداد تلاش‌های پرداخت زیاد بود؛ کمی بعد دوباره امتحان کنید.";
            return RedirectToPage(new { token });
        }

        var order = await orderService.GetOrderByIdAsync(View.OrderId);
        if (order is null)
            return NotFound();

        var start = await paymentFlow.StartOrderPaymentAsync(order, View.UserId.Value);
        if (!start.Succeeded)
        {
            ErrorMessage = start.Error;
            return RedirectToPage(new { token });
        }

        return Redirect(start.RedirectUrl!);
    }

    private async Task<bool> LoadAsync(string token)
    {
        var view = await staffOrders.GetPaymentViewAsync(token);
        if (view is null)
            return false;

        View = view;
        Token = token;
        IsOwner = CurrentUserId is { } me && me == view.UserId;
        return true;
    }
}
