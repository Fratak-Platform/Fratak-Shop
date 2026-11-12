using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Email;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>
/// صفحهٔ تأیید ایمیل (مرحلهٔ دوازدهم) – آدرس <c>/verify-email?token=…</c> از لینک داخل ایمیل «تأیید نشانی ایمیل».
/// باز کردن لینک فقط صفحه را نشان می‌دهد؛ تأیید با دکمهٔ «تأیید ایمیل» (POST) انجام می‌شود تا اسکنرهای ایمیل که لینک‌ها را
/// خودکار باز می‌کنند، بی‌اجازهٔ کاربر تأیید نکنند. ورود به حساب لازم نیست (توکن امضاشده خودش به کاربر و ایمیل گره خورده است).
/// منطق: <see cref="EmailVerification"/>.
/// </summary>
public class VerifyEmailModel(EmailVerification verification) : ShopPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    /// <summary>توکن معتبر و منقضی‌نشده است (دکمهٔ تأیید نمایش داده می‌شود).</summary>
    public bool IsValid { get; private set; }

    /// <summary>ایمیل نیمه‌پنهان برای نمایش. مثال: al***@gmail.com.</summary>
    public string? MaskedEmail { get; private set; }

    /// <summary>نتیجهٔ تأیید پس از POST (null = هنوز تأیید نشده).</summary>
    public bool? Confirmed { get; private set; }

    public string? Outcome { get; private set; }

    public void OnGet()
    {
        Read();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await verification.ConfirmAsync(Token, HttpContext.RequestAborted);
        Confirmed = Succeeded(result);
        Outcome = result.Message;
        Read();
        return Page();
    }

    private void Read()
    {
        ViewData[PageMetaKeys.Title] = "تأیید ایمیل";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
        var data = verification.ReadToken(Token);
        IsValid = data is not null;
        MaskedEmail = data is { } d ? EmailRules.Mask(d.Email) : null;
    }
}
