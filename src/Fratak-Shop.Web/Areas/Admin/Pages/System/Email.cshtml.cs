using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Email;
using Fratak_Shop.Web.Infrastructure.Team;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← سرویس ایمیل» (مرحلهٔ دوازدهم) – آدرس <c>/admin/system/email</c>؛ هم‌الگوی «سرویس پیامک».
/// <para>
/// تنظیم SMTP (سرور، درگاه، رمزنگاری، نام کاربری، رمز رمزنگاری‌شده، فرستنده، پاسخ به، نشانی سایت و سقف ساعتی) با «اتصال سریع»
/// (Gmail، ایمیل هاست، Zoho، …؛ assets/js/admin-email.js فیلدها را پر می‌کند)، «بررسی اتصال» (فقط ورود، بدون ارسال)،
/// «ایمیل آزمایشی» و «پیش‌نمایش» قالب ایمیل.
/// </para>
/// مشاهده: نظارت یا مدیریت فنی. ذخیره و آزمون: فقط مدیریت فنی؛ ذخیره «تأیید دوبارهٔ هویت» هم می‌خواهد چون رمز سرور ایمیل اینجاست.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class EmailModel(IEmailProviderService email, EmailDelivery delivery, EmailQueue queue, IMonitoringService monitoring) : AdminPageModel
{
    private const string PagePath = "/admin/system/email";

    public EmailProviderView Config { get; private set; } = null!;

    /// <summary>رمز از کجا می‌آید: «پنل مدیریت»، «تنظیمات سرور» (Email__Password) یا null.</summary>
    public string? PasswordSource { get; private set; }

    /// <summary>چرا ایمیل واقعی نمی‌رود (null = آماده).</summary>
    public string? NotReadyReason { get; private set; }

    public EmailDiagnostics Stats => delivery.Diagnostics;

    /// <summary>ایمیل‌های در صف (منتظر ارسال یا منتظر سقف ساعتی).</summary>
    public int Pending => queue.Pending;

    /// <summary>دامنهٔ فعلی سایت بدون www (برای «mail.{domain}» در اتصال سریع ایمیل هاست). مثال: www.fratak.ir → fratak.ir.</summary>
    public string Domain { get; private set; } = "";

    public sealed class EmailForm
    {
        public EmailMode Mode { get; set; }
        public string? Preset { get; set; }
        public string? Host { get; set; }
        public string? Port { get; set; }
        public EmailSecurity Security { get; set; }
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public bool ClearPassword { get; set; }
        public string? FromAddress { get; set; }
        public string? FromName { get; set; }
        public string? ReplyTo { get; set; }
        public string? SiteBaseUrl { get; set; }
        public string? MaxPerHour { get; set; }
    }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Config = await email.GetAsync(ct);
        var runtime = await email.GetRuntimeAsync(ct);
        PasswordSource = runtime.PasswordSource;
        NotReadyReason = runtime.NotReadyReason;
        var host = Request.Host.Host;
        Domain = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    [RequireReauth]
    public async Task<IActionResult> OnPostSaveAsync([FromForm(Name = "email")] EmailForm form)
    {
        var input = new EmailProviderInput(form.Mode, form.Preset, form.Host, form.Port, form.Security, form.UserName, form.Password, form.ClearPassword,
            form.FromAddress, form.FromName, form.ReplyTo, form.SiteBaseUrl, form.MaxPerHour);
        var result = await email.SaveAsync(input, $"{Request.Scheme}://{Request.Host}{Request.PathBase}", HttpContext.RequestAborted);
        if (Succeeded(result))
        {
            var passwordNote = form.ClearPassword ? "، حذف رمز" : string.IsNullOrEmpty(form.Password) ? "" : "، رمز جدید";
            await monitoring.LogActionAsync("EmailProviderSave", "System", null,
                $"ویرایش سرویس ایمیل ({form.Mode}، {EmailRules.OneLine(form.Host, 100)}{passwordNote})");
        }
        ShowResult(result);
        return Redirect(PagePath);
    }

    /// <summary>بررسی اتصال و ورود به سرور SMTP بدون ارسال ایمیل (حتی در حالت «فقط لاگ»).</summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostCheckAsync()
    {
        var attempt = await delivery.CheckAsync(HttpContext.RequestAborted);
        if (attempt.Ok)
            SuccessMessage = attempt.Message;
        else
            ErrorMessage = "بررسی اتصال ناموفق بود: " + attempt.Message;
        return Redirect(PagePath);
    }

    /// <summary>
    /// ارسال یک ایمیل واقعی به نشانی واردشده با همان قالب ایمیل‌های مشتری. در حالت «فقط لاگ» هم با SMTP ثبت‌شده ارسال می‌شود
    /// تا پیش از روشن کردن سرویس آزمایش شود. ارسال مستقیم است (نه از صف) تا نتیجه همین‌جا دیده شود.
    /// </summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostTestAsync(string? to)
    {
        var address = to?.Trim();
        if (!EmailRules.IsValidAddress(address))
        {
            ErrorMessage = "نشانی ایمیل معتبر نیست (مثال: name@example.com).";
            return Redirect(PagePath);
        }

        var attempt = await delivery.SendAsync(SampleMessage(address!, test: true), HttpContext.RequestAborted, forceSmtp: true);
        await monitoring.LogActionAsync("EmailProviderTest", "System", null,
            $"ایمیل آزمایشی به {EmailRules.Mask(address)}: {(attempt.Ok ? "موفق" : "ناموفق")}");
        if (attempt.Ok)
            SuccessMessage = $"ایمیل آزمایشی به {address} ارسال شد. اگر در صندوق ورودی نیست، پوشهٔ اسپم را هم ببینید.";
        else
            ErrorMessage = "ارسال ناموفق بود: " + attempt.Message;
        return Redirect(PagePath);
    }

    /// <summary>
    /// پیش‌نمایش HTML قالب ایمیل برای iframe صفحه (sandbox بدون اسکریپت). CSP این پاسخ هیچ اسکریپت، فرم یا اتصالی را اجازه نمی‌دهد.
    /// </summary>
    public async Task<IActionResult> OnGetPreviewAsync()
    {
        var html = await delivery.PreviewAsync(SampleMessage("customer@example.com", test: false), HttpContext.RequestAborted);
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src 'self' https: http: data:; style-src 'unsafe-inline'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";
        Response.Headers.CacheControl = "no-store";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>نمونهٔ پیام: آزمایشی یا یک اعلان سفارش نمونه برای پیش‌نمایش.</summary>
    private static EmailMessage SampleMessage(string to, bool test) => test
        ? new EmailMessage(to, "ایمیل آزمایشی فروشگاه",
            "این یک ایمیل آزمایشی از پنل مدیریت است.\nاگر این متن را می‌بینید، سرویس ایمیل فروشگاه درست تنظیم شده است.")
        {
            ActionUrl = "/",
            ActionText = "مشاهدهٔ فروشگاه",
            Category = "test",
        }
        : new EmailMessage(to, "سفارش شما ارسال شد",
            "سفارش ۱۰۲۴ تحویل پست شد.\nکد رهگیری مرسوله: ۱۲۳۴۵۶۷۸۹۰۱۲۳۴۵۶۷۸۹۰\nاز خرید شما سپاسگزاریم.")
        {
            ActionUrl = "/profile/orders",
            ActionText = "پیگیری سفارش",
            Category = "preview",
        };
}
